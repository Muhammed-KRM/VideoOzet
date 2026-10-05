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
  
  // Seri İçerik Planlama
  seriesOdak = '';
  seriesHedefKitle = 'genel';
  seriesEkTon = '';
  isPlanningSeries = false;
  planStatus: string = '';
  planProgress: number = 0;
  planError: string = '';
  
  contentResult: any = null;
  pastContentRequests: any[] = [];
  selectedRequestId: string = '';
  
  // İlerleme Durumu
  contentStatus: string = '';
  contentProgress: number = 0;
  contentError: string = '';
  private contentTimeoutId: any;

  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private apiService = inject(ApiService);
  private signalRService = inject(SignalRService);
  
  private subs: any[] = [];

  ngOnInit() {
    this.egitimId = this.route.snapshot.paramMap.get('id') || '';
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
      
      this.subs.push(
        this.signalRService.contentGenerated$.subscribe(data => {
          if (data && data.contentRequestId && this.isPlanningSeries) {
            this.contentProgress = 100;
            this.contentStatus = 'Tamamlandı! Sonuçlar yükleniyor...';
            this.apiService.getContentRequest(data.contentRequestId).subscribe(result => {
              this.isPlanningSeries = false;
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
          if (data && data.egitimId === this.egitimId && this.isPlanningSeries) {
            this.contentStatus = data.asama;
            this.contentProgress = data.yuzde;
            this.resetContentTimeout();
          }
        })
      );

      this.subs.push(
        this.signalRService.contentError$.subscribe(data => {
          if (data && data.egitimId === this.egitimId && this.isPlanningSeries) {
            this.contentError = 'İçerik üretilirken hata oluştu: ' + data.hataMesaji;
            this.isPlanningSeries = false;
            if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
          }
        })
      );
    }
  }

  private resetContentTimeout() {
    if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
    this.contentTimeoutId = setTimeout(() => {
      if (this.isPlanningSeries) {
        this.planError = 'İşlem beklediğimizden çok uzun sürdü. Lütfen işlemi iptal edip uygulamanızı yeniden başlatmayı deneyin.';
        this.isPlanningSeries = false;
      }
    }, 4 * 60 * 1000);
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
          baslik: d.dosyaAdi // dokumanlarda baslik yerine dosyaAdi var
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
          const completed = this.pastContentRequests.find(r => r.durum === 'Tamamlandi');
          if (completed) {
            this.selectContentRequest(completed.id);
          } else if (this.pastContentRequests[0]) {
            this.selectContentRequest(this.pastContentRequests[0].id);
          }
        }
      },
      error: (err) => console.error('Geçmiş içerikler yüklenemedi:', err)
    });
  }

  selectContentRequest(id: string) {
    if (!id) return;
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

  planSeries() {
    this.planError = '';
    
    if (this.egitim.islenmiVideoSayisi === 0) {
      this.planError = 'İçerik üretebilmek için en az 1 videonun işlemi tamamlanmış olmalıdır.';
      return;
    }

    this.isPlanningSeries = true;
    this.planStatus = 'Analiz isteği gönderiliyor...';
    this.planProgress = 10;
    
    if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);

    // 5 dakikalık zaman aşımı
    this.contentTimeoutId = setTimeout(() => {
      if (this.isPlanningSeries) {
        this.planError = 'İşlem beklediğimizden çok uzun sürdü. Arka plan servislerinde bir sorun olabilir.';
        this.isPlanningSeries = false;
      }
    }, 5 * 60 * 1000);
    
    const payload = {
      odak: this.seriesOdak,
      hedefKitle: this.seriesHedefKitle,
      ekTonTalimati: this.seriesEkTon
    };
    
    this.apiService.createSeriesPlan(this.egitimId, payload).subscribe({
      next: (res: any) => {
        this.planStatus = 'Harita planlaması yapılıyor... Lütfen bekleyin.';
        this.planProgress = 40;
        if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
        
        setTimeout(() => {
           this.router.navigate(['/series-planner', res.contentRequestId]);
        }, 1500); 
      },
      error: (err) => {
        console.error('İstek hatası:', err);
        this.isPlanningSeries = false;
        this.planProgress = 0;
        this.planError = 'İstek gönderilemedi. Sunucu bağlantısında sorun var.';
        if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
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
    
    if (!confirm(`${failedVideos.length} adet hatalı videoyu kaldığı yerden (transkript varsa özetlemeden) yeniden başlatmak istiyor musunuz?`)) return;
    
    failedVideos.forEach((v: any) => {
      this.retryVideo(v);
    });
  }

  resetQueue() {
    if (!confirm('RabbitMQ kuyruğu boşaltılacak ve eğitimdeki (Tamamlanmamış) tüm videolar yeniden sıraya eklenecektir. Bu işlem eski tıkanıklıkları çözer. Onaylıyor musunuz?')) return;
    
    this.apiService.resetQueue(this.egitimId).subscribe({
      next: (res) => {
        alert(res.message || 'Kuyruk sıfırlandı!');
        this.loadEgitim(); // Durumları güncelle
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
