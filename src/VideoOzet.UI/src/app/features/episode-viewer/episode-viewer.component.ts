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
  hedefAlan: 'VideoPlani' | 'ArastirmaOzeti' | 'Hepsi' = 'Hepsi';
  seciliRevizyonId: string | null = null;
  isRevising = false;
  isCancelling = false;
  
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private apiService = inject(ApiService);
  private signalRService = inject(SignalRService);

  private subs: Subscription[] = [];

  ngOnInit() {
    this.contentRequestId = this.route.snapshot.paramMap.get('id') || '';
    this.planNo = Number(this.route.snapshot.paramMap.get('planNo')) || 0;
    this.bolumNo = Number(this.route.snapshot.paramMap.get('bolumNo')) || 0;

    if (!this.contentRequestId || !this.planNo || !this.bolumNo) {
      this.router.navigate(['/dashboard']);
      return;
    }

    this.signalRService.startConnection();
    this.setupSignalRSubscriptions();
    this.loadData();
    this.startPolling();
  }

  private setupSignalRSubscriptions() {
    this.subs.push(
      this.signalRService.pipelineStageChanged$.subscribe((data: any) => {
        if (!data) return;
        if (!data.contentRequestId || data.contentRequestId === this.contentRequestId) {
          this.loadData();
        }
      })
    );

    this.subs.push(
      this.signalRService.seriesVideoGenerated$.subscribe((data: any) => {
        if (!data || data.contentRequestId === this.contentRequestId) {
          this.loadData();
        }
      })
    );

    this.subs.push(
      this.signalRService.contentProgress$.subscribe((data: any) => {
        if (!data || data.contentRequestId === this.contentRequestId) {
          this.loadData();
        }
      })
    );
  }

  ngOnDestroy() {
    this.stopPolling();
    this.subs.forEach(s => s.unsubscribe());
    this.subs = [];
  }

  loadData() {
    this.apiService.getEpisodeDetails(this.contentRequestId, this.planNo, this.bolumNo).subscribe({
      next: (res) => {
        this.episode = res;
        this.isLoading = false;

        // BolumDurumu API'den STRING gelir (JsonStringEnumConverter)
        const isStillWorking = this.episode.durum === 'Isleniyor' || this.episode.durum === 'Bekliyor' || this.isRevising;
        if (isStillWorking) {
          if (!this.isPolling) this.startPolling();
        } else {
          this.stopPolling();
          this.isRevising = false;
        }
      },
      error: (err) => {
        console.warn('Bölüm detayları yükleme hatası (arka planda periyodik olarak tekrar denenecek):', err);
        this.isLoading = false;
        // Polling'i durdurmuyoruz, geçici ağ gecikmelerinde kurtarsın
      }
    });
  }

  getBaglamSorunlari(): string[] {
    const raw = this.episode?.baglamSorunlariJson;
    if (!raw) return [];
    if (Array.isArray(raw)) return raw.map((x: any) => String(x));
    if (typeof raw === 'string') {
      const trimmed = raw.trim();
      if (!trimmed) return [];
      try {
        const parsed = JSON.parse(trimmed);
        if (Array.isArray(parsed)) return parsed.map((x: any) => typeof x === 'string' ? x : (x?.mesaj || x?.aciklama || JSON.stringify(x)));
        if (typeof parsed === 'object' && parsed !== null) {
          return [parsed.mesaj || parsed.aciklama || JSON.stringify(parsed)];
        }
      } catch {
        return [trimmed];
      }
    }
    return [];
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

  selectRevision(rev: any) {
    if (rev && rev.id) {
      this.seciliRevizyonId = rev.id;
    }
  }

  resetToLatestRevision() {
    this.seciliRevizyonId = this.episode?.aktifRevizyonId || null;
  }

  getAktifRevizyon() {
    if (!this.episode || !this.episode.revizyonlar || this.episode.revizyonlar.length === 0) return null;
    
    // Kullanıcının manuel olarak seçtiği revizyon varsa onu göster
    if (this.seciliRevizyonId) {
      const found = this.episode.revizyonlar.find((r: any) => r.id === this.seciliRevizyonId);
      if (found) return found;
    }

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
    if (!this.revizyonTalimati && this.episode?.durum !== 'Hata') return;
    if (this.episode.durum === 'Isleniyor') return; // Zaten işleniyor

    const currentRev = this.getAktifRevizyon();
    this.isRevising = true;
    this.apiService.reviseEpisode(this.contentRequestId, this.bolumNo, { 
      talimat: this.revizyonTalimati || '',
      hedefAlan: this.hedefAlan,
      bazRevizyonNo: currentRev?.revizyonNo
    }, this.planNo).subscribe({
      next: () => {
        this.revizyonTalimati = '';
        this.seciliRevizyonId = null; // En son revizyon gösterilmek üzere sıfırla
        this.startPolling();
      },
      error: (err) => {
        console.error('Revizyon başarısız', err);
        this.isRevising = false;
        alert('Revizyon isteği başarısız oldu.');
      }
    });
  }

  retryEpisode() {
    if (this.episode?.durum === 'Isleniyor' || this.isRevising) return;
    this.isRevising = true;
    this.apiService.retryEpisode(this.contentRequestId, this.bolumNo, this.planNo).subscribe({
      next: () => {
        this.revizyonTalimati = '';
        this.seciliRevizyonId = null;
        if (this.episode) {
          this.episode.durum = 'Isleniyor';
        }
        this.startPolling();
      },
      error: (err) => {
        console.error('Yeniden üretim başarısız', err);
        this.isRevising = false;
        alert('Yeniden üretim isteği başarısız oldu.');
      }
    });
  }

  onReQcCompleted() {
    this.loadData();
  }

  cancelProcess() {
    if (!this.contentRequestId || this.isCancelling) return;
    if (!confirm('Devam eden bölüm üretim/revizyon işlemini durdurmak istediğinize emin misiniz?')) return;

    this.isCancelling = true;
    this.apiService.cancelSeriesProcess(this.contentRequestId).subscribe({
      next: () => {
        this.isCancelling = false;
        this.isRevising = false;
        this.loadData();
      },
      error: (err) => {
        console.error('İşlem durdurma hatası', err);
        this.isCancelling = false;
        alert('İşlem durdurulamadı: ' + (err.error?.mesaj || err.message));
      }
    });
  }

  goBackToPlan() {
    this.router.navigate(['/series-planner', this.contentRequestId], {
      queryParams: this.planNo > 0 ? { planNo: this.planNo } : undefined
    });
  }
}
