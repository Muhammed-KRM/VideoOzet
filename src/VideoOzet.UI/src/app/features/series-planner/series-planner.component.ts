import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { SeriesMapComponent } from '../series-map/series-map.component';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-series-planner',
  standalone: true,
  imports: [CommonModule, FormsModule, SeriesMapComponent],
  templateUrl: './series-planner.component.html'
})
export class SeriesPlannerComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private apiService = inject(ApiService);
  private signalRService = inject(SignalRService);

  contentRequestId: string = '';
  data: any = null;
  isLoading = true;
  isPolling = false;
  pollInterval: any;

  // Taslak Form
  draftOptions = {
    videoSayisi: null,
    varsayilanSureDk: null,
    talimat: ''
  };
  isDrafting = false;

  private signalRSub?: Subscription;

  ngOnInit() {
    this.contentRequestId = this.route.snapshot.paramMap.get('id') || '';
    if (!this.contentRequestId) {
      this.router.navigate(['/dashboard']);
      return;
    }

    this.signalRService.startConnection();
    this.loadData();
    this.startPolling();

    // Listen to real-time updates if needed, though polling might be enough for now.
    // In a real app, you would rely on signalR events to avoid polling.
  }

  ngOnDestroy() {
    this.stopPolling();
    if (this.signalRSub) {
      this.signalRSub.unsubscribe();
    }
  }

  loadData() {
    this.apiService.getSeriesPlan(this.contentRequestId).subscribe({
      next: (res) => {
        this.data = res;
        this.isLoading = false;

        if (this.data?.konuAnalizi?.beklenenKonularJson) {
          try {
            this.data.konuAnalizi.beklenenKonular = JSON.parse(this.data.konuAnalizi.beklenenKonularJson);
          } catch {
            this.data.konuAnalizi.beklenenKonular = [];
          }
        }

        // ContentRequestDurumu: Bekliyor = 0 (Analiz yapılıyor)
        // SeriPlanDurumu: Taslak = 0, Olusturuluyor = 1
        const reqDurum = this.data.durum; // 0: Bekliyor, 1: IcerikUretiliyor
        const planDurum = this.data.guncelPlan?.durum; // 0: Taslak, 1: Olusturuluyor, vb.

        if (reqDurum === 0 || planDurum === 1) {
          // Hala islemde
          if (!this.isPolling) this.startPolling();
        } else {
          this.stopPolling();
          this.isDrafting = false;
        }
      },
      error: (err) => {
        console.error('Plan yüklenemedi', err);
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

  requestNewDraft() {
    if (!this.data) return;
    this.isDrafting = true;
    this.apiService.generatePlanDraft(this.contentRequestId, this.draftOptions).subscribe({
      next: () => {
        this.startPolling();
      },
      error: (err) => {
        console.error('Taslak isteği başarısız', err);
        this.isDrafting = false;
        alert('Yeni taslak isteği başarısız oldu.');
      }
    });
  }

  approvePlan() {
    if (!this.data?.guncelPlan) return;
    if (!confirm('Bu planı onaylıyor musunuz? Onayladıktan sonra planlama aşaması kapanır ve ilk videonun üretimi başlar.')) return;

    this.apiService.approvePlan(this.contentRequestId, this.data.guncelPlan.planNo).subscribe({
      next: () => {
        alert('Plan onaylandı, üretim başladı!');
        // Bölüm izleme sayfasına yönlendir veya burada kal
        this.loadData();
      },
      error: (err) => {
        alert('Plan onaylanırken hata oluştu.');
      }
    });
  }
}
