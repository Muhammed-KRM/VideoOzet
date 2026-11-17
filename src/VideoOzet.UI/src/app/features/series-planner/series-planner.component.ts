import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ApiService, CourseSourceCardDto } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { SeriesMapComponent } from '../series-map/series-map.component';
import { StudioQaPanelComponent } from '../../shared/components/studio-qa-panel/studio-qa-panel.component';
import { Subscription, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

export type TopicFilterState = 'normal' | 'zorunlu' | 'haric';

@Component({
  selector: 'app-series-planner',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, SeriesMapComponent, StudioQaPanelComponent],
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
  isCancelling = false;
  isQaOpen = false;
  plannerOrnekSorular = [
    'Atanmayan kaynakları neden hiçbir bölüme dahil etmedin, hangi bölüme eklenebilir?',
    'Bölümler arası konu akışı ve geçişlerde herhangi bir kopukluk veya tekrar var mı?',
    'İslam düşüncesi ve modern algoritmik kuşatma dengesi bölümlere nasıl dağıtıldı?'
  ];
  pollInterval: any;
  currentStatusMessage: string = '';

  // Eğitimdeki tüm kaynaklar (API'den veya fallback olarak eğitim uçlarından)
  egitimKaynaklari: CourseSourceCardDto[] = [];
  private loadedFallbackEgitimId: string | null = null;

  // Beklenen konular için 3 durumlu filtre: normal -> zorunlu -> haric
  topicFilterStates: Record<string, TopicFilterState> = {};

  // Ayrıştırılmış analiz ve plan listeleri
  kaynaktaOlmayanlar: string[] = [];
  disaridaBirakilanlar: string[] = [];

  // Taslak Form
  draftOptions: {
    videoSayisi: number | null;
    varsayilanSureDk: number | null;
    talimat: string;
    mevcutMetinleriKoru: boolean;
  } = {
    videoSayisi: null,
    varsayilanSureDk: null,
    talimat: '',
    mevcutMetinleriKoru: true
  };
  selectedPlanNo: number | null = null;
  isDrafting = false;
  onayTalimati = '';
  isApproving = false;

  private subs: Subscription[] = [];

  ngOnInit() {
    this.contentRequestId = this.route.snapshot.paramMap.get('id') || '';
    const qpPlanNo = Number(this.route.snapshot.queryParamMap.get('planNo'));
    if (qpPlanNo > 0) {
      this.selectedPlanNo = qpPlanNo;
    }
    if (!this.contentRequestId) {
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
        if (data.mesaj) {
          this.currentStatusMessage = data.mesaj;
        }
        if (!data.contentRequestId || data.contentRequestId === this.contentRequestId || (this.data && data.egitimId === this.data.egitimId)) {
          this.loadData();
        }
      })
    );

    this.subs.push(
      this.signalRService.seriesPlanGenerated$.subscribe((data: any) => {
        if (!data || data.contentRequestId === this.contentRequestId) {
          this.currentStatusMessage = 'Plan hazır!';
          this.isDrafting = false;
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
      this.signalRService.topicAnalysisCompleted$.subscribe((data: any) => {
        if (!data || data.contentRequestId === this.contentRequestId) {
          this.currentStatusMessage = 'Konu analizi tamamlandı. Plan taslağı üretiliyor...';
          this.loadData();
        }
      })
    );

    this.subs.push(
      this.signalRService.contentProgress$.subscribe((data: any) => {
        if (!data || data.contentRequestId === this.contentRequestId) {
          if (data.asama) {
            this.currentStatusMessage = `${data.asama} (${data.yuzde || 0}%)`;
          }
          this.loadData();
        }
      })
    );

    this.subs.push(
      this.signalRService.contentError$.subscribe((data: any) => {
        if (data && data.contentRequestId === this.contentRequestId) {
          console.warn('SignalR içerik hatası:', data.hataMesaji);
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

  get tumPlanlar(): any[] {
    return Array.isArray(this.data?.tumPlanlar) ? this.data.tumPlanlar : [];
  }

  get nextPlanVersionNo(): number {
    if (this.tumPlanlar.length === 0) return (this.data?.guncelPlan?.planNo || 1) + 1;
    return Math.max(...this.tumPlanlar.map((p: any) => p.planNo || 1)) + 1;
  }

  selectPlanVersion(planNo: number) {
    if (!planNo || this.selectedPlanNo === planNo) return;
    this.selectedPlanNo = planNo;
    this.loadData();
  }

  loadData() {
    this.apiService.getSeriesPlan(this.contentRequestId, this.selectedPlanNo).subscribe({
      next: (res) => {
        this.data = res;
        this.isLoading = false;

        this.parseAnalysisAndPlanLists();
        this.syncEgitimKaynaklari();

        if (this.isStillProcessing()) {
          if (!this.isPolling) this.startPolling();
        } else {
          this.stopPolling();
          this.isDrafting = false;
        }
      },
      error: (err) => {
        console.warn('Plan yükleme hatası (arka planda periyodik olarak tekrar denenecek):', err);
        this.isLoading = false;
      }
    });
  }

  private parseStringList(raw: any): string[] {
    if (!raw) return [];
    if (Array.isArray(raw)) {
      return raw.map(item => (typeof item === 'string' ? item.trim() : (item?.konu || item?.baslik || JSON.stringify(item)))).filter(Boolean);
    }
    if (typeof raw === 'string') {
      const trimmed = raw.trim();
      if (!trimmed) return [];
      try {
        const parsed = JSON.parse(trimmed);
        if (Array.isArray(parsed)) {
          return parsed.map(item => (typeof item === 'string' ? item.trim() : (item?.konu || item?.baslik || JSON.stringify(item)))).filter(Boolean);
        }
      } catch {
        return trimmed.split(/\r?\n/).map(s => s.trim()).filter(Boolean);
      }
    }
    return [];
  }

  private parseAnalysisAndPlanLists() {
    const analiz = this.data?.konuAnalizi;
    if (analiz) {
      analiz.beklenenKonular = this.parseStringList(analiz.beklenenKonular ?? analiz.beklenenKonularJson);
      this.kaynaktaOlmayanlar = this.parseStringList(analiz.kaynaktaOlmayanlar ?? analiz.kaynaktaOlmayanlarJson);

      for (const k of analiz.beklenenKonular) {
        if (!this.topicFilterStates[k]) {
          this.topicFilterStates[k] = 'normal';
        }
      }
    } else {
      this.kaynaktaOlmayanlar = [];
    }

    const plan = this.data?.guncelPlan;
    if (plan) {
      this.disaridaBirakilanlar = this.parseStringList(plan.disaridaBirakilanlar ?? plan.disaridaBirakilanlarJson);
    } else {
      this.disaridaBirakilanlar = [];
    }
  }

  private syncEgitimKaynaklari() {
    const rawList = this.data?.egitimKaynaklari ?? this.data?.kaynaklar;
    if (Array.isArray(rawList) && rawList.length > 0) {
      this.egitimKaynaklari = rawList.map((k: any) => ({
        kaynakId: String(k.kaynakId || k.id || ''),
        kaynakTuru: k.kaynakTuru || k.tip || 'Kaynak',
        baslik: k.baslik || k.dosyaAdi || 'İsimsiz Kaynak',
        dil: k.dil || k.orijinalDil || 'tr',
        kullanimTuru: String(k.kullanimTuru ?? 'DogrudanReferans'),
        tahminiToplamSureDk: k.tahminiToplamSureDk,
        konular: Array.isArray(k.konular) ? k.konular : [],
        ozetBilgi: k.ozetBilgi || ''
      }));
      return;
    }

    const egitimId = this.data?.egitimId;
    if (egitimId && this.loadedFallbackEgitimId !== egitimId) {
      this.loadedFallbackEgitimId = egitimId;
      forkJoin({
        videos: this.apiService.getEgitimVideos(egitimId).pipe(catchError(() => of([]))),
        docs: this.apiService.getEgitimDokumanlar(egitimId).pipe(catchError(() => of([])))
      }).subscribe(({ videos, docs }) => {
        if (this.data?.egitimKaynaklari?.length) return;
        const combined: CourseSourceCardDto[] = [];
        for (const v of videos || []) {
          combined.push({
            kaynakId: String(v.id),
            kaynakTuru: 'Video',
            baslik: v.baslik || v.dosyaAdi || 'Video',
            dil: v.orijinalDil || 'tr',
            kullanimTuru: v.kullanimTuru === 1 || v.kullanimTuru === 'SadeceArgumanVeMantik' ? 'SadeceArgumanVeMantik' : 'DogrudanReferans'
          });
        }
        for (const d of docs || []) {
          combined.push({
            kaynakId: String(d.id),
            kaynakTuru: 'Dokuman',
            baslik: d.dosyaAdi || d.baslik || 'Doküman',
            dil: d.orijinalDil || 'tr',
            kullanimTuru: d.kullanimTuru === 1 || d.kullanimTuru === 'SadeceArgumanVeMantik' ? 'SadeceArgumanVeMantik' : 'DogrudanReferans'
          });
        }
        this.egitimKaynaklari = combined;
      });
    }
  }

  // ── Beklenen Konular Tıklanabilir Durum Yönetimi ──

  toggleTopicState(konu: string) {
    const current = this.topicFilterStates[konu] || 'normal';
    if (current === 'normal') {
      this.topicFilterStates[konu] = 'zorunlu';
    } else if (current === 'zorunlu') {
      this.topicFilterStates[konu] = 'haric';
    } else {
      this.topicFilterStates[konu] = 'normal';
    }
  }

  getTopicState(konu: string): TopicFilterState {
    return this.topicFilterStates[konu] || 'normal';
  }

  get zorunluKonular(): string[] {
    return Object.entries(this.topicFilterStates)
      .filter(([_, state]) => state === 'zorunlu')
      .map(([k]) => k);
  }

  get haricKonular(): string[] {
    return Object.entries(this.topicFilterStates)
      .filter(([_, state]) => state === 'haric')
      .map(([k]) => k);
  }

  // ── Kaynak Kapsam Özeti Hesaplamaları ──

  private getAssignedSourceIdsAndTitles(): { ids: Set<string>; titles: Set<string> } {
    const ids = new Set<string>();
    const titles = new Set<string>();
    const bolumler = this.data?.guncelPlan?.seriBolumler || [];

    for (const b of bolumler) {
      const rawList = b.atananKaynakIdleri ?? b.atananKaynaklar ?? b.atananKaynaklarJson;
      let items: any[] = [];
      if (Array.isArray(rawList)) {
        items = rawList;
      } else if (typeof rawList === 'string' && rawList.trim()) {
        try {
          const parsed = JSON.parse(rawList);
          if (Array.isArray(parsed)) items = parsed;
        } catch {}
      }

      for (const item of items) {
        if (typeof item === 'string') {
          const clean = item.trim();
          if (clean) {
            ids.add(clean.toLowerCase());
            titles.add(clean.toLowerCase());
          }
        } else if (item && typeof item === 'object') {
          const id = String(item.kaynakId || item.id || '').trim().toLowerCase();
          const title = String(item.baslik || item.dosyaAdi || '').trim().toLowerCase();
          if (id) ids.add(id);
          if (title) titles.add(title);
        }
      }
    }

    return { ids, titles };
  }

  get toplamKaynakSayisi(): number {
    return this.egitimKaynaklari.length;
  }

  get atananKaynakSayisi(): number {
    const { ids, titles } = this.getAssignedSourceIdsAndTitles();
    if (this.egitimKaynaklari.length === 0) {
      return ids.size;
    }
    let matched = 0;
    for (const src of this.egitimKaynaklari) {
      const idMatch = src.kaynakId && ids.has(src.kaynakId.toLowerCase());
      const titleMatch = src.baslik && titles.has(src.baslik.toLowerCase());
      if (idMatch || titleMatch) {
        matched++;
      }
    }
    return matched;
  }

  get atanmayanKaynaklar(): CourseSourceCardDto[] {
    if (this.egitimKaynaklari.length === 0) return [];
    const { ids, titles } = this.getAssignedSourceIdsAndTitles();
    return this.egitimKaynaklari.filter(src => {
      const idMatch = src.kaynakId && ids.has(src.kaynakId.toLowerCase());
      const titleMatch = src.baslik && titles.has(src.baslik.toLowerCase());
      return !idMatch && !titleMatch;
    });
  }

  /** Arka planda hâlâ bir işlem (analiz / plan üretimi / bölüm üretimi) sürüyor mu? */
  isStillProcessing(): boolean {
    const d = this.data;
    if (!d) return true;

    if (this.isDrafting) return true;

    if (d.durum === 'IcerikUretiliyor' || d.durum === 'QcYapiliyor') {
      return true;
    }

    const analiz = d.konuAnalizi;
    if (!analiz) {
      return d.durum !== 'Hata';
    }
    if (analiz.durum === 'Hata') {
      return false;
    }
    if (analiz.durum === 'Isleniyor' || analiz.durum === 'Bekliyor') {
      return true;
    }

    const plan = d.guncelPlan;
    if (!plan) {
      return analiz.durum !== 'Hata';
    }
    if (plan.durum === 'Hata') {
      return false;
    }

    if (plan.durum === 'Olusturuluyor' || plan.durum === 'Taslak') {
      return true;
    }

    if (plan.onaylandi) {
      const bolumler = plan.seriBolumler || [];
      const anyPendingOrProcessing = bolumler.some(
        (b: any) => b.durum === 'Isleniyor' || b.durum === 'Bekliyor'
      );
      return anyPendingOrProcessing;
    }

    return false;
  }

  get isPlanGenerating(): boolean {
    const d = this.data;
    if (!d) return true;
    if (this.isDrafting) return true;

    if (!d.konuAnalizi || d.konuAnalizi.durum === 'Isleniyor' || d.konuAnalizi.durum === 'Bekliyor') {
      return true;
    }

    if (d.durum === 'Hata' || d.konuAnalizi.durum === 'Hata' || d.guncelPlan?.durum === 'Hata') {
      return false;
    }

    return !d.guncelPlan || d.guncelPlan.durum === 'Olusturuluyor' || d.guncelPlan.durum === 'Taslak' || (d.guncelPlan.seriBolumler?.length ?? 0) === 0;
  }

  get hasError(): boolean {
    const d = this.data;
    if (!d) return false;

    if (d.guncelPlan && (d.guncelPlan.seriBolumler?.length ?? 0) > 0) {
      return false;
    }

    if (this.isDrafting || this.isPlanGenerating || this.isStillProcessing()) {
      return false;
    }

    return d.durum === 'Hata' || d.konuAnalizi?.durum === 'Hata' || d.guncelPlan?.durum === 'Hata';
  }

  get canApprove(): boolean {
    const p = this.data?.guncelPlan;
    return !!p && !p.onaylandi && p.durum === 'OnayBekliyor' && (p.seriBolumler?.length ?? 0) > 0;
  }

  startPolling() {
    if (this.isPolling) return;
    this.isPolling = true;
    this.pollInterval = setInterval(() => {
      this.loadData();
    }, 4000);
  }

  stopPolling() {
    this.isPolling = false;
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
  }

  cancelProcess() {
    if (!this.contentRequestId || this.isCancelling) return;
    if (!confirm('Devam eden işlemi durdurmak istediğinize emin misiniz?')) return;

    this.isCancelling = true;
    this.apiService.cancelSeriesProcess(this.contentRequestId).subscribe({
      next: () => {
        this.isCancelling = false;
        this.isDrafting = false;
        this.isApproving = false;
        this.stopPolling();
        this.currentStatusMessage = 'İşlem kullanıcı tarafından durduruldu.';
        this.loadData();
      },
      error: (err) => {
        console.error('İşlem durdurma hatası', err);
        this.isCancelling = false;
        alert('İşlem durdurulamadı: ' + (err.error?.mesaj || err.message));
      }
    });
  }

  requestNewDraft(isNewVersionFromApproved = false) {
    if (!this.data || this.isDrafting) return;
    this.isDrafting = true;
    // Yeni versiyon oluşturulunca en son versiyonu otomatik göster
    if (isNewVersionFromApproved) {
      this.selectedPlanNo = null;
    }
    this.currentStatusMessage = isNewVersionFromApproved
      ? `Eski plan ve dökümanlar korunarak yeni Plan v${this.nextPlanVersionNo} taslağı oluşturuluyor...`
      : 'Seçilen kısıtlar ve konu filtreleriyle yeni plan taslağı oluşturuluyor...';

    const payload = {
      videoSayisi: this.draftOptions.videoSayisi,
      varsayilanSureDk: this.draftOptions.varsayilanSureDk,
      talimat: this.draftOptions.talimat || '',
      revizyonTalimati: this.draftOptions.talimat || '',
      zorunluKonular: this.zorunluKonular,
      haricKonular: this.haricKonular,
      mevcutMetinleriKoru: isNewVersionFromApproved ? this.draftOptions.mevcutMetinleriKoru : false
    };

    this.apiService.generatePlanDraft(this.contentRequestId, payload).subscribe({
      next: () => {
        this.startPolling();
        this.loadData();
      },
      error: (err) => {
        console.error('Taslak isteği başarısız', err);
        this.isDrafting = false;
        alert('Yeni taslak isteği başarısız oldu: ' + (err.error?.mesaj || err.message));
      }
    });
  }

  approvePlan() {
    if (!this.data?.guncelPlan || this.isApproving) return;
    if (!confirm('Bu planı onaylıyor musunuz? Onayladıktan sonra planlama aşaması kapanır ve bölümlerin üretimi başlar.')) return;

    this.isApproving = true;
    this.apiService.approvePlan(this.contentRequestId, this.data.guncelPlan.planNo, this.onayTalimati).subscribe({
      next: () => {
        this.isApproving = false;
        alert('Plan onaylandı, üretim başladı!');
        this.startPolling();
        this.loadData();
      },
      error: (err) => {
        this.isApproving = false;
        console.error('Plan onaylama hatası', err);
        alert('Plan onaylanırken hata oluştu: ' + (err.error?.mesaj || err.message));
      }
    });
  }

  printToPdf() {
    if (!this.data || !this.data.guncelPlan) return;
    
    const plan = this.data.guncelPlan;
    const analiz = this.data.konuAnalizi;
    
    let html = `<html><head><title>Video Serisi Planı</title><style>
      body { font-family: sans-serif; line-height: 1.6; color: #333; max-width: 800px; margin: 0 auto; padding: 20px; }
      h1, h2, h3 { color: #111; }
      .section { margin-bottom: 30px; }
      .episode { border: 1px solid #ddd; padding: 15px; margin-bottom: 15px; border-radius: 5px; }
      .episode h3 { margin-top: 0; }
    </style></head><body>`;
    
    html += `<h1>Video Serisi Planı</h1>`;
    
    if (analiz) {
      html += `<div class="section"><h2>AI İçerik Analizi</h2>`;
      html += `<p><strong>Ana Fikir:</strong> ${analiz.anaFikir || '-'}</p>`;
      
      if (analiz.beklenenKonular?.length) {
        html += `<p><strong>Beklenen Konular:</strong></p><ul>`;
        analiz.beklenenKonular.forEach((k: string) => { html += `<li>${k}</li>`; });
        html += `</ul>`;
      }
      
      if (analiz.oneriGerekcesi) {
        html += `<p><strong>AI Önerisi:</strong> ${analiz.oneriGerekcesi}</p>`;
      }
      html += `</div>`;
    }
    
    html += `<div class="section"><h2>Bölümler</h2>`;
    const bolumler = [...(plan.seriBolumler || [])].sort((a: any, b: any) => a.bolumNo - b.bolumNo);
    
    if (bolumler.length === 0) {
      html += `<p>Henüz bölüm bulunmuyor.</p>`;
    } else {
      bolumler.forEach((b: any) => {
        html += `<div class="episode">`;
        html += `<h3>Bölüm ${b.bolumNo}: ${b.calismaBasligi}</h3>`;
        html += `<p><strong>Hedef Süre:</strong> ~${b.hedefSureDk} dk<br>`;
        if (b.kanca) {
          html += `<strong>Açılış Kancası:</strong> ${b.kanca}<br>`;
        }
        html += `<strong>Durum:</strong> ${b.durum}<br>`;
        html += `<strong>Ana Fikir:</strong> ${b.anaFikir || '-'}</p>`;
        
        let konular: string[] = [];
        if (b.konularJson) {
          try { konular = JSON.parse(b.konularJson); } catch {}
        }
        if (konular.length) {
          html += `<p><strong>Konular:</strong></p><ul>`;
          konular.forEach(k => { html += `<li>${k}</li>`; });
          html += `</ul>`;
        }
        html += `</div>`;
      });
    }
    
    html += `</div></body></html>`;
    
    const printWindow = window.open('', '_blank');
    if (printWindow) {
      printWindow.document.write(html);
      printWindow.document.close();
      printWindow.focus();
      setTimeout(() => {
        printWindow.print();
      }, 250);
    } else {
      alert("Lütfen popup (açılır pencere) engelleyiciyi kapatıp tekrar deneyin.");
    }
  }

  downloadPlan(format: 'md' | 'txt' | 'doc' | 'docx' = 'md') {
    if (!this.data || !this.data.guncelPlan) return;

    const plan = this.data.guncelPlan;
    const analiz = this.data.konuAnalizi;
    const bolumler = [...(plan.seriBolumler || [])].sort((a: any, b: any) => a.bolumNo - b.bolumNo);
    
    if (format === 'docx' || format === 'doc') {
      import('docx').then(({ Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType }) => {
        import('file-saver').then(({ saveAs }) => {
          
          const children: any[] = [];
          
          children.push(
            new Paragraph({
              text: "Video Serisi Planı",
              heading: HeadingLevel.HEADING_1,
              alignment: AlignmentType.CENTER,
            })
          );
          
          if (analiz) {
            children.push(new Paragraph({ text: "AI İçerik Analizi", heading: HeadingLevel.HEADING_2 }));
            children.push(new Paragraph({
              children: [
                new TextRun({ text: "Ana Fikir: ", bold: true }),
                new TextRun({ text: analiz.anaFikir || '-' })
              ]
            }));
            
            if (analiz.beklenenKonular?.length) {
              children.push(new Paragraph({
                children: [new TextRun({ text: "Beklenen Konular:", bold: true })]
              }));
              analiz.beklenenKonular.forEach((k: string) => {
                children.push(new Paragraph({
                  children: [new TextRun({ text: k })],
                  bullet: { level: 0 }
                }));
              });
            }
            
            if (analiz.oneriGerekcesi) {
              children.push(new Paragraph({
                children: [
                  new TextRun({ text: "AI Önerisi: ", bold: true }),
                  new TextRun({ text: analiz.oneriGerekcesi })
                ]
              }));
            }
          }
          
          children.push(new Paragraph({ text: "Bölümler", heading: HeadingLevel.HEADING_2 }));
          
          if (bolumler.length === 0) {
            children.push(new Paragraph({ text: "Henüz bölüm bulunmuyor." }));
          } else {
            bolumler.forEach((b: any) => {
              children.push(new Paragraph({ text: `Bölüm ${b.bolumNo}: ${b.calismaBasligi}`, heading: HeadingLevel.HEADING_3 }));
              children.push(new Paragraph({
                children: [
                  new TextRun({ text: "Hedef Süre: ", bold: true }),
                  new TextRun({ text: `~${b.hedefSureDk} dk` })
                ]
              }));
              if (b.kanca) {
                children.push(new Paragraph({
                  children: [
                    new TextRun({ text: "Açılış Kancası: ", bold: true }),
                    new TextRun({ text: b.kanca })
                  ]
                }));
              }
              children.push(new Paragraph({
                children: [
                  new TextRun({ text: "Durum: ", bold: true }),
                  new TextRun({ text: b.durum })
                ]
              }));
              children.push(new Paragraph({
                children: [
                  new TextRun({ text: "Ana Fikir: ", bold: true }),
                  new TextRun({ text: b.anaFikir || '-' })
                ]
              }));
              
              let konular: string[] = [];
              if (b.konularJson) { try { konular = JSON.parse(b.konularJson); } catch {} }
              if (konular.length) {
                children.push(new Paragraph({
                  children: [new TextRun({ text: "Konular:", bold: true })]
                }));
                konular.forEach(k => {
                  children.push(new Paragraph({
                    children: [new TextRun({ text: k })],
                    bullet: { level: 0 }
                  }));
                });
              }
              children.push(new Paragraph({ text: "" }));
            });
          }
          
          const doc = new Document({
            sections: [{ children }]
          });
          
          Packer.toBlob(doc).then((blob) => {
            saveAs(blob, `SeriPlani_${this.contentRequestId}_Plan${plan.planNo}.docx`);
          });
        });
      });
      return;
    }

    let content = '';
    let mimeType = 'text/plain;charset=utf-8';
    let extension = format;
    
    if (format === 'md') mimeType = 'text/markdown;charset=utf-8';
      
    content = format === 'md' ? `# Video Serisi Planı\n\n` : `VIDEO SERISI PLANI\n==================\n\n`;

    if (analiz) {
      content += format === 'md' ? `## AI İçerik Analizi\n\n` : `AI Icerik Analizi\n-----------------\n\n`;
      content += format === 'md' ? `**Ana Fikir:**\n${analiz.anaFikir || '-'}\n\n` : `Ana Fikir:\n${analiz.anaFikir || '-'}\n\n`;
      
      if (analiz.beklenenKonular && analiz.beklenenKonular.length > 0) {
        content += format === 'md' ? `**Beklenen Konular:**\n` : `Beklenen Konular:\n`;
        analiz.beklenenKonular.forEach((k: string) => {
          content += `- ${k}\n`;
        });
        content += `\n`;
      }

      if (analiz.oneriGerekcesi) {
        content += format === 'md' ? `**AI Önerisi:**\n${analiz.oneriGerekcesi}\n\n` : `AI Onerisi:\n${analiz.oneriGerekcesi}\n\n`;
      }
      content += `---\n\n`;
    }

    content += format === 'md' ? `## Bölümler\n\n` : `Bolumler\n--------\n\n`;

    if (bolumler.length === 0) {
      content += `Henüz bölüm bulunmuyor.\n`;
    } else {
      bolumler.forEach((b: any) => {
        content += format === 'md' ? `### Bölüm ${b.bolumNo}: ${b.calismaBasligi}\n` : `[ Bolum ${b.bolumNo}: ${b.calismaBasligi} ]\n`;
        content += format === 'md' ? `- **Hedef Süre:** ~${b.hedefSureDk} dk\n` : `- Hedef Sure: ~${b.hedefSureDk} dk\n`;
        if (b.kanca) {
          content += format === 'md' ? `- **Açılış Kancası:** ${b.kanca}\n` : `- Acilis Kancasi: ${b.kanca}\n`;
        }
        content += format === 'md' ? `- **Durum:** ${b.durum}\n` : `- Durum: ${b.durum}\n`;
        content += format === 'md' ? `- **Ana Fikir:** ${b.anaFikir || '-'}\n` : `- Ana Fikir: ${b.anaFikir || '-'}\n`;
        
        let konular: string[] = [];
        if (b.konularJson) {
          try {
            konular = JSON.parse(b.konularJson);
          } catch {}
        }
        if (konular.length > 0) {
          content += format === 'md' ? `- **Konular:**\n` : `- Konular:\n`;
          konular.forEach(k => {
            content += `  - ${k}\n`;
          });
        }
        content += `\n`;
      });
    }
    const blob = new Blob([content], { type: mimeType });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `SeriPlani_${this.contentRequestId}_Plan${plan.planNo}.${extension}`;
    a.click();
    window.URL.revokeObjectURL(url);
  }
}
