import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
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
          // Gerçekte gelen mesaja göre update yapılır, MVP için listeyi refresh yapıyoruz.
          this.loadEgitim();
        })
      );
      
      this.subs.push(
        this.signalRService.contentGenerated$.subscribe(data => {
          this.isRequestingContent = false;
          this.contentResult = data;
        })
      );
    }
  }

  loadEgitim() {
    this.apiService.getEgitim(this.egitimId).subscribe({
      next: (data) => {
        this.egitim = data;
        this.isLoading = false;
      },
      error: () => {
        this.router.navigate(['/dashboard']);
      }
    });
  }

  requestContent() {
    if (!this.contentTopic) return;
    this.isRequestingContent = true;
    this.contentResult = null;
    
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
      }
    });
  }

  goBack() {
    this.router.navigate(['/dashboard']);
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }
}
