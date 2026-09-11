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
  
  // İçerik Talep Formu
  contentTopic = '';
  contentLength = 'orta';
  contentAudience = 'genel';
  isRequestingContent = false;
  
  contentResult: any = null;
  
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
          if (data && data.contentRequestId && this.isRequestingContent) {
            this.contentProgress = 100;
            this.contentStatus = 'Tamamlandı! Sonuçlar yükleniyor...';
            this.apiService.getContentRequest(data.contentRequestId).subscribe(result => {
              this.isRequestingContent = false;
              this.contentResult = result;
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
      },
      error: () => {
        this.router.navigate(['/dashboard']);
      }
    });
  }

  onUploadComplete() {
    this.loadEgitim();
  }

  requestContent() {
    if (!this.contentTopic) return;
    this.isRequestingContent = true;
    this.contentResult = null;
    this.contentStatus = 'Sıraya Alındı, Bekleniyor...';
    this.contentProgress = 5;
    this.contentError = '';
    
    if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);

    // 5 dakikalık zaman aşımı
    this.contentTimeoutId = setTimeout(() => {
      if (this.isRequestingContent) {
        this.contentError = 'İşlem beklediğimizden çok uzun sürdü. Arka plan servislerinde (RabbitMQ veya Worker) bir sorun olabilir. Lütfen işlemi iptal edip uygulamanızı yeniden başlatmayı deneyin.';
        this.isRequestingContent = false;
      }
    }, 5 * 60 * 1000);
    
    const payload = {
      konu: this.contentTopic,
      hedefUzunluk: this.contentLength,
      hedefKitle: this.contentAudience
    };
    
    this.apiService.createContentRequest(this.egitimId, payload).subscribe({
      next: (res) => {
        // Backend Accepted döner, sonuc SignalR'dan gelecek.
      },
      error: () => {
        this.isRequestingContent = false;
        this.contentError = 'İstek gönderilemedi. Sunucu bağlantısında sorun var.';
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

  goBack() {
    this.router.navigate(['/dashboard']);
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
    if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
  }
}
