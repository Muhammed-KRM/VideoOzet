import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { PlanViewerComponent } from '../plan-viewer/plan-viewer.component';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-episode-viewer',
  standalone: true,
  imports: [CommonModule, FormsModule, PlanViewerComponent],
  templateUrl: './episode-viewer.component.html'
})
export class EpisodeViewerComponent implements OnInit, OnDestroy {
  contentRequestId = '';
  planNo = 0;
  bolumNo = 0;
  
  episode: any = null;
  isLoading = true;
  isPolling = false;
  pollInterval: any;

  revizyonTalimati = '';
  isRevising = false;
  
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private apiService = inject(ApiService);
  private signalRService = inject(SignalRService);

  ngOnInit() {
    this.contentRequestId = this.route.snapshot.paramMap.get('id') || '';
    this.planNo = Number(this.route.snapshot.paramMap.get('planNo')) || 0;
    this.bolumNo = Number(this.route.snapshot.paramMap.get('bolumNo')) || 0;

    if (!this.contentRequestId || !this.planNo || !this.bolumNo) {
      this.router.navigate(['/dashboard']);
      return;
    }

    this.signalRService.startConnection();
    this.loadData();
    this.startPolling();
  }

  ngOnDestroy() {
    this.stopPolling();
  }

  loadData() {
    this.apiService.getEpisodeDetails(this.contentRequestId, this.planNo, this.bolumNo).subscribe({
      next: (res) => {
        this.episode = res;
        this.isLoading = false;

        // BolumDurumu API'den STRING gelir (JsonStringEnumConverter)
        if (this.episode.durum === 'Isleniyor') {
          if (!this.isPolling) this.startPolling();
        } else {
          this.stopPolling();
          this.isRevising = false;
        }
      },
      error: (err) => {
        console.error('Bölüm detayları yüklenemedi', err);
        this.isLoading = false;
        this.stopPolling();
      }
    });
  }

  startPolling() {
    if (this.isPolling) return;
    this.isPolling = true;
    this.pollInterval = setInterval(() => {
      this.loadData();
    }, 5000);
  }

  stopPolling() {
    this.isPolling = false;
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
  }

  getAktifRevizyon() {
    if (!this.episode || !this.episode.revizyonlar || this.episode.revizyonlar.length === 0) return null;
    // AktifRevizyonId varsa onu, yoksa en son versiyonu al
    if (this.episode.aktifRevizyonId) {
      return this.episode.revizyonlar.find((r: any) => r.id === this.episode.aktifRevizyonId) || this.episode.revizyonlar[0];
    }
    // En yüksek revizyonNo'ya sahip olanı bul
    return this.episode.revizyonlar.reduce((prev: any, current: any) => {
      return (prev.revizyonNo > current.revizyonNo) ? prev : current;
    });
  }

  requestRevision() {
    if (!this.revizyonTalimati) return;
    if (this.episode.durum === 'Isleniyor') return; // Zaten işleniyor
    
    this.isRevising = true;
    this.apiService.reviseEpisode(this.contentRequestId, this.bolumNo, { talimat: this.revizyonTalimati }).subscribe({
      next: () => {
        this.revizyonTalimati = '';
        this.startPolling();
      },
      error: (err) => {
        console.error('Revizyon başarısız', err);
        this.isRevising = false;
        alert('Revizyon isteği başarısız oldu.');
      }
    });
  }

  goBackToPlan() {
    this.router.navigate(['/series-planner', this.contentRequestId]);
  }
}
