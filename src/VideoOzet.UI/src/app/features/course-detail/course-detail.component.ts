import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { VideoUploadComponent } from '../video-upload/video-upload.component';
import { ContentResultComponent } from '../content-result/content-result.component';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-course-detail',
  standalone: true,
  imports: [CommonModule, VideoUploadComponent, ContentResultComponent, FormsModule],
  templateUrl: './course-detail.component.html'
})
export class CourseDetailComponent implements OnInit, OnDestroy {
  egitim: any = null;
  egitimId: string = '';
  isLoading = true;

  // Mod Seçimi: 'classic' (⚡ Hızlı İçerik) | 'planner' (✨ Akıllı Seri Planlayıcı)
  selectedMode: 'classic' | 'planner' = 'classic';
  
  // 1. Klasik Mod (Tek Video - Fast Vector RAG)
  contentTopic: string = '';
  contentLength: string = 'orta';
  contentAudience: string = 'genel';
  isRequestingContent: boolean = false;
  contentStatus: string = '';
  contentProgress: number = 0;
  contentError: string = '';
  private contentTimeoutId: any;

  // 2. Akıllı Planlayıcı Modu (Çoklu Video Serisi)
  seriesOdak: string = '';
  seriesHedefKitle: string = 'genel';
  seriesEkTon: string = '';
  isPlanningSeries: boolean = false;
  planStatus: string = '';
  planProgress: number = 0;
  planError: string = '';
  
  contentResult: any = null;
  pastContentRequests: any[] = [];
  selectedRequestId: string = '';

  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private apiService = inject(ApiService);
  private signalRService = inject(SignalRService);
  
  private subs: any[] = [];

  ngOnInit() {
    this.egitimId = this.route.snapshot.paramMap.get('id') || '';

    // Kullanıcının kayıtlı mod tercihini yükle
    const savedMode = localStorage.getItem('videoozet.icerikModu');
    if (savedMode === 'classic' || savedMode === 'planner') {
      this.selectedMode = savedMode;
    }

    if (this.egitimId) {
      this.loadEgitim();
      this.signalRService.startConnection();
      
      this.subs.push(
        this.signalRService.pipelineStageChanged$.subscribe(data => {
          if (this.egitim && this.egitim.videos) {
            const video = this.egitim.videos.find((v: any) => v.id === data.videoId);
            if (video) {
              video.sonAsama = data.asama;
              video.durum = data.durum;
              video.hataMesaji = data.mesaj;
            }
          }
        })
      );
      
      // Klasik mod SignalR bildirimleri
      this.subs.push(
        this.signalRService.contentGenerated$.subscribe(data => {
          if (data && data.contentRequestId && this.isRequestingContent) {
            this.contentProgress = 100;
            this.contentStatus = 'Tamamlandı! Sonuçlar yükleniyor...';
            this.apiService.getContentRequest(data.contentRequestId).subscribe(result => {
              this.isRequestingContent = false;
              this.contentResult = result;
              this.selectedRequestId = data.contentRequestId;
              this.loadPastRequests(false);
              if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
            });
          }
        })
      );

      this.subs.push(
        this.signalRService.contentProgress$.subscribe(data => {
          if (data && data.egitimId === this.egitimId && this.isRequestingContent) {
            this.contentStatus = data.asama;
            this.contentProgress = data.yuzde;
            this.resetContentTimeout();
          }
        })
      );

      this.subs.push(
        this.signalRService.contentError$.subscribe(data => {
          if (data && data.egitimId === this.egitimId && this.isRequestingContent) {
            this.contentError = 'İçerik üretilirken hata oluştu: ' + data.hataMesaji;
            this.isRequestingContent = false;
            if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
          }
        })
      );
    }
  }

  setMode(mode: 'classic' | 'planner') {
    this.selectedMode = mode;
    localStorage.setItem('videoozet.icerikModu', mode);
  }

  private resetContentTimeout() {
    if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
    this.contentTimeoutId = setTimeout(() => {
      if (this.isRequestingContent) {
        this.contentError = 'İşlem beklediğimizden çok uzun sürdü. Arka plan servislerinde bir sorun olabilir. Lütfen işlemi iptal edip uygulamanızı yeniden başlatmayı deneyin.';
        this.isRequestingContent = false;
      }
    }, 5 * 60 * 1000);
  }

  loadEgitim() {
    forkJoin({
      egitim: this.apiService.getEgitim(this.egitimId),
      videos: this.apiService.getEgitimVideos(this.egitimId),
      dokumanlar: this.apiService.getEgitimDokumanlar(this.egitimId)
    }).subscribe({
      next: (data) => {
        this.egitim = data.egitim;
        // Merge videos and dokumans for display
        const vids = (data.videos || []).map((v: any) => ({ ...v, tip: 'video' }));
        const docs = (data.dokumanlar || []).map((d: any) => ({ 
          ...d, 
          tip: 'dokuman',
          baslik: d.dosyaAdi
        }));
        
        this.egitim.videos = [...vids, ...docs].sort((a, b) => {
          return new Date(b.olusturmaTarihi).getTime() - new Date(a.olusturmaTarihi).getTime();
        });
        
        this.isLoading = false;
        this.loadPastRequests(true);
      },
      error: () => {
        this.router.navigate(['/dashboard']);
      }
    });
  }

  loadPastRequests(autoSelectFirst: boolean = false) {
    this.apiService.getContentRequests(this.egitimId).subscribe({
      next: (requests) => {
        this.pastContentRequests = requests || [];
        if (autoSelectFirst && this.pastContentRequests.length > 0 && !this.contentResult) {
          const classicCompleted = this.pastContentRequests.find(r => r.durum === 'Tamamlandi' && (r.mod === 0 || r.modAdi === 'Klasik' || !r.mod));
          if (classicCompleted) {
            this.selectContentRequest(classicCompleted.id);
          } else if (this.pastContentRequests[0] && (this.pastContentRequests[0].mod === 0 || this.pastContentRequests[0].modAdi === 'Klasik')) {
            this.selectContentRequest(this.pastContentRequests[0].id);
          }
        }
      },
      error: (err) => console.error('Geçmiş içerikler yüklenemedi:', err)
    });
  }

  selectContentRequest(id: string) {
    if (!id) return;
    const req = this.pastContentRequests.find(r => r.id === id);
    if (req && (req.mod === 1 || req.modAdi === 'Planli')) {
      // Akıllı seri talebi: Seri planlayıcı sayfasına yönlendir
      this.router.navigate(['/series-planner', id]);
      return;
    }

    // Klasik talep: İçerik sonucunu sayfada göster
    this.selectedRequestId = id;
    this.apiService.getContentRequest(id).subscribe({
      next: (result) => {
        this.contentResult = result;
      },
      error: (err) => console.error('İçerik detayı yüklenemedi:', err)
    });
  }

  onUploadComplete() {
    this.loadEgitim();
  }

  // 1. Klasik Tekil İçerik Üretimi (2fc9178)
  requestContent() {
    if (!this.contentTopic) return;
    this.isRequestingContent = true;
    this.contentResult = null;
    this.contentStatus = 'Sıraya Alındı, Bekleniyor...';
    this.contentProgress = 5;
    this.contentError = '';
    
    if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);

    this.contentTimeoutId = setTimeout(() => {
      if (this.isRequestingContent) {
        this.contentError = 'İşlem beklediğimizden çok uzun sürdü. Arka plan servislerinde bir sorun olabilir.';
        this.isRequestingContent = false;
      }
    }, 5 * 60 * 1000);
    
    const payload = {
      konu: this.contentTopic,
      hedefUzunluk: this.contentLength,
      hedefKitle: this.contentAudience
    };
    
    this.apiService.createContentRequest(this.egitimId, payload).subscribe({
      next: () => {
        // Backend Accepted döner, sonuç SignalR'dan gelecek.
      },
      error: () => {
        this.isRequestingContent = false;
        this.contentError = 'İstek gönderilemedi. Sunucu bağlantısında sorun var.';
        if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
      }
    });
  }

  // 2. Akıllı Çoklu Video Serisi Planlama
  planSeries() {
    this.planError = '';
    
    if (this.egitim.islenmiVideoSayisi === 0) {
      this.planError = 'İçerik planlayabilmek için en az 1 videonun işlemi tamamlanmış olmalıdır.';
      return;
    }

    this.isPlanningSeries = true;
    this.planStatus = 'Analiz isteği gönderiliyor...';
    this.planProgress = 10;
    
    const payload = {
      odak: this.seriesOdak,
      hedefKitle: this.seriesHedefKitle,
      ekTonTalimati: this.seriesEkTon
    };
    
    this.apiService.createSeriesPlan(this.egitimId, payload).subscribe({
      next: (res: any) => {
        this.planStatus = 'Harita planlaması yapılıyor... Lütfen bekleyin.';
        this.planProgress = 40;
        
        setTimeout(() => {
          this.router.navigate(['/series-planner', res.contentRequestId]);
        }, 1200); 
      },
      error: (err) => {
        console.error('İstek hatası:', err);
        this.isPlanningSeries = false;
        this.planProgress = 0;
        this.planError = 'İstek gönderilemedi. Sunucu bağlantısında sorun var.';
      }
    });
  }

  deleteItem(item: any) {
    const itemName = item.baslik || item.dosyaAdi || 'bu dosyayı';
    if (!confirm(`"${itemName}" dosyasını silmek / iptal etmek istediğinize emin misiniz?`)) {
      return;
    }

    const obs = item.tip === 'dokuman'
      ? this.apiService.deleteDokuman(this.egitimId, item.id)
      : this.apiService.deleteVideo(this.egitimId, item.id);

    obs.subscribe({
      next: () => {
        this.loadEgitim();
      },
      error: (err) => {
        console.error('Silme hatası:', err);
        alert('Dosya silinirken bir hata oluştu.');
      }
    });
  }

  retryVideo(item: any) {
    if (item.tip !== 'video') return;
    
    item.durum = 'Bekliyor';
    item.islemDurumu = 'Bekliyor';
    item.sonAsama = 'Yeniden Başlatılıyor';
    item.hataMesaji = null;
    
    this.apiService.retryVideo(this.egitimId, item.id).subscribe({
      next: () => {
        // Backend'den eventler gelecek
      },
      error: (err) => {
        console.error('Yeniden başlatma hatası:', err);
        alert('Yeniden başlatılırken hata oluştu.');
      }
    });
  }

  retryAllFailed() {
    if (!this.egitim || !this.egitim.videos) return;
    
    const failedVideos = this.egitim.videos.filter((v: any) => v.tip === 'video' && (v.islemDurumu === 'Hata' || v.durum === 'Hata'));
    
    if (failedVideos.length === 0) {
      alert('Yeniden başlatılacak hatalı video bulunamadı.');
      return;
    }
    
    if (!confirm(`${failedVideos.length} adet hatalı videoyu kaldığı yerden yeniden başlatmak istiyor musunuz?`)) return;
    
    failedVideos.forEach((v: any) => {
      this.retryVideo(v);
    });
  }

  resetQueue() {
    if (!confirm('RabbitMQ kuyruğu boşaltılacak ve eğitimdeki (Tamamlanmamış) tüm videolar yeniden sıraya eklenecektir. Bu işlem eski tıkanıklıkları çözer. Onaylıyor musunuz?')) return;
    
    this.apiService.resetQueue(this.egitimId).subscribe({
      next: (res) => {
        alert(res.message || 'Kuyruk sıfırlandı!');
        this.loadEgitim();
      },
      error: (err) => {
        console.error('Kuyruk sıfırlama hatası:', err);
        alert('Kuyruk sıfırlanırken hata oluştu.');
      }
    });
  }

  goBack() {
    this.router.navigate(['/dashboard']);
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
    if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
  }
}
