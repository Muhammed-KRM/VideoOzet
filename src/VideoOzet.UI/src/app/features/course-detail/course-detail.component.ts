import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { VideoUploadComponent } from '../video-upload/video-upload.component';
import { ContentResultComponent } from '../content-result/content-result.component';
import { FormsModule } from '@angular/forms';
import { marked } from 'marked';
import { markdownToDocxBlob, DocxSection } from '../../core/utils/markdown-docx';

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

  // 3. Kaynak Detay Modalı (Transkript, Damıtılmış Özet, Terim Korumalı Çeviri)
  isKaynakModalOpen: boolean = false;
  isLoadingKaynakDetay: boolean = false;
  kaynakDetay: any = null;
  kaynakActiveTab: 'ozet' | 'cevirisi' | 'ham' = 'ozet';
  isDownloadMenuOpen: boolean = false;
  isGeneratingDocx: boolean = false;
  copiedToClipboard: boolean = false;

  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private apiService = inject(ApiService);
  private signalRService = inject(SignalRService);
  
  private subs: any[] = [];

  private pollIntervalId: any;
  isRecovering: boolean = false;
  recoveryFeedback: string = '';
  videoProgressMap: { [videoId: string]: { yuzde?: number, mevcutAdim?: number, toplamAdim?: number, mesaj?: string, sonAsama?: string, durum?: string } } = {};

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
          if (data && data.videoId) {
            const existing = this.videoProgressMap[data.videoId] || {};
            const isError = data.durum === 'Hata' || data.asama === 'Hata';

            this.videoProgressMap[data.videoId] = {
              ...existing,
              sonAsama: data.asama || existing.sonAsama,
              durum: data.durum || existing.durum,
              mesaj: data.mesaj || existing.mesaj,
              yuzde: (data.yuzde !== undefined && data.yuzde !== null) ? data.yuzde : existing.yuzde,
              mevcutAdim: (data.mevcutAdim !== undefined && data.mevcutAdim !== null) ? data.mevcutAdim : existing.mevcutAdim,
              toplamAdim: (data.toplamAdim !== undefined && data.toplamAdim !== null) ? data.toplamAdim : existing.toplamAdim
            };

            if (this.egitim && this.egitim.videos) {
              const video = this.egitim.videos.find((v: any) => v.id === data.videoId);
              if (video) {
                video.sonAsama = this.videoProgressMap[data.videoId].sonAsama;
                video.durum = this.videoProgressMap[data.videoId].durum;
                video.mesaj = this.videoProgressMap[data.videoId].mesaj;
                video.hataMesaji = isError ? data.mesaj : null;
                if (!isError) {
                  video.takiliMi = false;
                  video.takilmaNedeni = null;
                }
                video.yuzde = this.videoProgressMap[data.videoId].yuzde;
                video.mevcutAdim = this.videoProgressMap[data.videoId].mevcutAdim;
                video.toplamAdim = this.videoProgressMap[data.videoId].toplamAdim;
              }
            }
          }
        })
      );

      // Tamamlanmamış dosyalar varken her 4 saniyede bir otomatik senkronize et (takılma & ilerleme kontrolü)
      this.pollIntervalId = setInterval(() => {
        if (this.egitimId && this.needsPolling) {
          this.loadEgitim(true);
        }
      }, 4000);
      
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
        this.contentError = 'İçerik üretim işlemi zaman aşımına uğradı. Lütfen tekrar deneyin.';
        this.isRequestingContent = false;
      }
    }, 180000);
  }

  ngOnDestroy() {
    if (this.pollIntervalId) clearInterval(this.pollIntervalId);
    if (this.contentTimeoutId) clearTimeout(this.contentTimeoutId);
    this.subs.forEach(s => s?.unsubscribe?.());
  }

  isItemCompleted(v: any): boolean {
    return v?.islemDurumu === 'Tamamlandi' || v?.durum === 'Tamamlandi' || v?.islemDurumu === 8;
  }

  isItemError(v: any): boolean {
    return v?.islemDurumu === 'Hata' || v?.durum === 'Hata' || v?.islemDurumu === 9;
  }

  isItemStalled(v: any): boolean {
    return !this.isItemCompleted(v) && !this.isItemError(v) && !!v?.takiliMi;
  }

  get completedVideosCount(): number {
    if (!this.egitim || !this.egitim.videos) return 0;
    return this.egitim.videos.filter((v: any) => this.isItemCompleted(v)).length;
  }

  get totalVideosCount(): number {
    return this.egitim?.videos?.length || 0;
  }

  get completionPercentage(): number {
    if (!this.totalVideosCount) return 0;
    return Math.round((this.completedVideosCount / this.totalVideosCount) * 100);
  }

  get stalledItemsCount(): number {
    if (!this.egitim || !this.egitim.videos) return 0;
    return this.egitim.videos.filter((v: any) => this.isItemStalled(v)).length;
  }

  get failedItemsCount(): number {
    if (!this.egitim || !this.egitim.videos) return 0;
    return this.egitim.videos.filter((v: any) => this.isItemError(v)).length;
  }

  get problematicItemsCount(): number {
    return this.stalledItemsCount + this.failedItemsCount;
  }

  get hasActiveProcessing(): boolean {
    if (!this.egitim || !this.egitim.videos) return false;
    return this.egitim.videos.some((v: any) =>
      !this.isItemCompleted(v) && !this.isItemError(v) && !v.takiliMi
    );
  }

  get needsPolling(): boolean {
    if (!this.egitim || !this.egitim.videos) return false;
    return this.completedVideosCount < this.totalVideosCount;
  }

  getVideoStatusText(v: any): string {
    if (this.isItemCompleted(v)) return 'Tamamlandı';
    if (this.isItemError(v)) return 'Hata';
    if (this.isItemStalled(v)) return '⚠️ Takılı Kaldı';
    if (v.sonAsama) return v.sonAsama;
    if (v.islemDurumu === 'SttBasladi' || v.islemDurumu === 3) return 'Ses Çözümleniyor (STT)';
    if (v.islemDurumu === 'SttTamamlandi' || v.islemDurumu === 4) return 'STT Bitti';
    if (v.islemDurumu === 'OzetlemeBasladi' || v.islemDurumu === 5) return 'Özet Çıkarılıyor';
    if (v.islemDurumu === 'OzetlemeTamamlandi' || v.islemDurumu === 6) return 'Özet Bitti';
    if (v.islemDurumu === 'IndekslemeBasladi' || v.islemDurumu === 7) return 'Vektör İndeksleniyor';
    return 'Bekliyor';
  }

  getVideoDetailSubtitle(v: any): string {
    if (this.isItemCompleted(v)) return '✅ İndekslendi (RAG Hazır)';
    if (this.isItemError(v)) {
      return '❌ ' + (v.hataMesaji || 'İşlem başarısız oldu — yeniden başlatabilirsiniz.');
    }
    if (this.isItemStalled(v)) {
      return '⚠️ ' + (v.takilmaNedeni || 'Arka plan işlemi yanıt vermiyor — tek tıkla kurtarabilirsiniz.');
    }
    const cached = this.videoProgressMap[v.id];
    if (v.mesaj) return v.mesaj;
    if (cached?.mesaj) return cached.mesaj;
    if (v.islemDurumu === 'SttBasladi' || v.islemDurumu === 3) return '🎙️ Ses metne dökülüyor (STT)...';
    if (v.islemDurumu === 'SttTamamlandi' || v.islemDurumu === 4 || v.islemDurumu === 'OzetlemeBasladi' || v.islemDurumu === 5) return '📝 Özet ve kavramlar çıkarılıyor...';
    if (v.islemDurumu === 'IndekslemeBasladi' || v.islemDurumu === 7) return '🧠 Vektör veritabanına indeksleniyor...';
    return '⏳ Kuyrukta işleniyor...';
  }

  getVideoPercentage(v: any): number {
    if (this.isItemCompleted(v)) return 100;
    if (this.isItemError(v)) return 0;
    const cached = this.videoProgressMap[v.id];
    const yuzde = v.yuzde ?? cached?.yuzde;
    if (yuzde !== undefined && yuzde !== null && yuzde > 0) return yuzde;
    
    // Aşamaya göre standart ilerleme yüzdeleri
    if (v.islemDurumu === 'SttBasladi' || v.islemDurumu === 3) return 30;
    if (v.islemDurumu === 'SttTamamlandi' || v.islemDurumu === 4) return 60;
    if (v.islemDurumu === 'OzetlemeBasladi' || v.islemDurumu === 5) return 75;
    if (v.islemDurumu === 'OzetlemeTamamlandi' || v.islemDurumu === 6) return 85;
    if (v.islemDurumu === 'IndekslemeBasladi' || v.islemDurumu === 7) return 92;
    return 10;
  }

  getVideoProgressText(v: any): string {
    if (this.isItemCompleted(v)) return '100%';
    const cached = this.videoProgressMap[v.id];
    const mevcut = v.mevcutAdim ?? cached?.mevcutAdim;
    const toplam = v.toplamAdim ?? cached?.toplamAdim;
    if (mevcut !== undefined && toplam !== undefined && toplam > 0) {
      return `${mevcut}/${toplam} Parça (%${this.getVideoPercentage(v)})`;
    }
    return `%${this.getVideoPercentage(v)}`;
  }

  loadEgitim(silent: boolean = false) {
    if (!silent) this.isLoading = true;
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
        
        const allItems = [...vids, ...docs].sort((a, b) => {
          return new Date(b.olusturmaTarihi).getTime() - new Date(a.olusturmaTarihi).getTime();
        });

        // Per-item live progress retention without overriding server-side stalled/error/completed state
        allItems.forEach((item: any) => {
          if (item.takiliMi || this.isItemCompleted(item) || this.isItemError(item)) {
            delete this.videoProgressMap[item.id];
            return;
          }

          const cached = this.videoProgressMap[item.id];
          if (cached) {
            if (cached.yuzde !== undefined) item.yuzde = cached.yuzde;
            if (cached.mevcutAdim !== undefined) item.mevcutAdim = cached.mevcutAdim;
            if (cached.toplamAdim !== undefined) item.toplamAdim = cached.toplamAdim;
            if (cached.mesaj !== undefined) item.mesaj = cached.mesaj;
            if (cached.sonAsama !== undefined) item.sonAsama = cached.sonAsama;
          }
        });

        this.egitim.videos = allItems;
        
        this.isLoading = false;
        if (!silent) {
          this.loadPastRequests(true);
        }
      },
      error: () => {
        if (!silent) this.router.navigate(['/dashboard']);
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

  /** Akıllı seri (Planlı mod) talepleri — en yeni üstte. */
  get seriesRequests(): any[] {
    return this.pastContentRequests.filter(r => r.mod === 1 || r.modAdi === 'Planli');
  }

  /** Seri talebinin kullanıcıya gösterilecek durumu (plan durumu esas alınır). */
  seriesStatus(req: any): { label: string; cls: string } {
    if (req.durum === 'Hata') return { label: 'Hata', cls: 'bg-rose-500/15 text-rose-300 border-rose-500/30' };
    switch (req.planDurum) {
      case 'OnayBekliyor': return { label: 'Onay Bekliyor', cls: 'bg-amber-500/15 text-amber-300 border-amber-500/30' };
      case 'Onaylandi':
        return req.durum === 'Tamamlandi'
          ? { label: 'Tamamlandı', cls: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30' }
          : { label: 'Üretimde', cls: 'bg-indigo-500/15 text-indigo-300 border-indigo-500/30' };
      case 'Hata': return { label: 'Hata', cls: 'bg-rose-500/15 text-rose-300 border-rose-500/30' };
      case 'Iptal': return { label: 'İptal', cls: 'bg-slate-600/30 text-slate-300 border-slate-500/30' };
      case 'Olusturuluyor':
      case 'Taslak': return { label: 'Planlanıyor', cls: 'bg-purple-500/15 text-purple-300 border-purple-500/30' };
      default: return { label: 'Analiz Ediliyor', cls: 'bg-purple-500/15 text-purple-300 border-purple-500/30' };
    }
  }

  openSeries(id: string) {
    this.router.navigate(['/series-planner', id]);
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

  retryItem(item: any) {
    if (!item) return;

    delete this.videoProgressMap[item.id];
    item.takiliMi = false;
    item.takilmaNedeni = null;
    item.hataMesaji = null;
    item.durum = 'Bekliyor';
    item.islemDurumu = 'Bekliyor';
    item.sonAsama = 'Kuyruğa Alındı';
    item.mesaj = '⏳ Yeniden başlatıldı, sıra bekleniyor...';

    const obs = item.tip === 'dokuman'
      ? this.apiService.retryDokuman(this.egitimId, item.id)
      : this.apiService.retryVideo(this.egitimId, item.id);

    obs.subscribe({
      next: () => {
        this.loadEgitim(true);
      },
      error: (err) => {
        console.error('Yeniden başlatma hatası:', err);
        alert('Yeniden başlatılırken hata oluştu.');
      }
    });
  }

  retryVideo(item: any) {
    this.retryItem(item);
  }

  recoverProblematicItems() {
    if (this.isRecovering) return;
    this.isRecovering = true;
    this.recoveryFeedback = '';

    this.apiService.recoverProblematicFiles(this.egitimId).subscribe({
      next: (res) => {
        this.isRecovering = false;
        this.recoveryFeedback = res?.message || 'Sorunlu dosyalar yeniden sıraya alındı.';
        if (this.egitim?.videos) {
          this.egitim.videos.forEach((v: any) => {
            if (this.isItemError(v) || this.isItemStalled(v)) {
              delete this.videoProgressMap[v.id];
            }
          });
        }
        this.loadEgitim(true);
        setTimeout(() => {
          this.recoveryFeedback = '';
        }, 8000);
      },
      error: (err) => {
        this.isRecovering = false;
        console.error('Toplu kurtarma hatası:', err);
        alert('Sorunlu dosyalar kurtarılırken bir hata oluştu.');
      }
    });
  }

  retryAllFailed() {
    this.recoverProblematicItems();
  }

  resetQueue() {
    if (!confirm('RabbitMQ kuyruğu boşaltılacak ve eğitimdeki (Tamamlanmamış) tüm video ve dokümanlar yeniden sıraya eklenecektir. Bu işlem eski tıkanıklıkları çözer. Onaylıyor musunuz?')) return;
    
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

  // --- Kaynak Kullanım Türü & Detay Modal İşlemleri ---

  toggleKullanimTuru(item: any, event?: MouseEvent) {
    if (event) event.stopPropagation();
    const current = (item.kullanimTuru === 1 || item.kullanimTuru === 'SadeceArgumanVeMantik') ? 1 : 0;
    const next = current === 1 ? 0 : 1;
    this.apiService.updateKaynakKullanimTuru(this.egitimId, item.tip, item.id, next).subscribe({
      next: () => {
        item.kullanimTuru = next;
        if (this.kaynakDetay && this.kaynakDetay.id === item.id) {
          this.kaynakDetay.kullanimTuru = next;
        }
      },
      error: (err) => {
        console.error('Kullanım türü güncellenemedi:', err);
        alert('Kullanım türü güncellenirken hata oluştu.');
      }
    });
  }

  isArgumanMode(item: any): boolean {
    return item?.kullanimTuru === 1 || item?.kullanimTuru === 'SadeceArgumanVeMantik';
  }

  openKaynakDetayModal(item: any) {
    this.isKaynakModalOpen = true;
    this.isLoadingKaynakDetay = true;
    this.kaynakDetay = null;
    this.isDownloadMenuOpen = false;
    this.copiedToClipboard = false;

    const req$ = item.tip === 'video'
      ? this.apiService.getVideoIcerik(this.egitimId, item.id)
      : this.apiService.getDokumanIcerik(this.egitimId, item.id);

    req$.subscribe({
      next: (res) => {
        this.kaynakDetay = res;
        this.isLoadingKaynakDetay = false;
        if (res.orijinalDil && res.orijinalDil.toLowerCase() !== 'tr' && res.cevrilmisOzetMetni) {
          this.kaynakActiveTab = 'cevirisi';
        } else if (res.damitilmisOzetMetni) {
          this.kaynakActiveTab = 'ozet';
        } else {
          this.kaynakActiveTab = 'ham';
        }
      },
      error: (err) => {
        console.error('Kaynak içeriği alınamadı:', err);
        alert('Kaynak içeriği yüklenirken bir hata oluştu.');
        this.isKaynakModalOpen = false;
        this.isLoadingKaynakDetay = false;
      }
    });
  }

  closeKaynakModal() {
    this.isKaynakModalOpen = false;
    this.kaynakDetay = null;
    this.isDownloadMenuOpen = false;
  }

  setKaynakTab(tab: 'ozet' | 'cevirisi' | 'ham') {
    this.kaynakActiveTab = tab;
  }

  toggleDownloadMenu() {
    this.isDownloadMenuOpen = !this.isDownloadMenuOpen;
  }

  get activeKaynakText(): string {
    if (!this.kaynakDetay) return '';
    if (this.kaynakActiveTab === 'cevirisi') {
      return this.kaynakDetay.cevrilmisOzetMetni || this.kaynakDetay.damitilmisOzetMetni || '';
    }
    if (this.kaynakActiveTab === 'ozet') {
      return this.kaynakDetay.damitilmisOzetMetni || '';
    }
    return this.kaynakDetay.hamMetin || '';
  }

  formatMarkdown(text: string): string {
    if (!text) return '';
    return marked.parse(text) as string;
  }

  copyKaynakText() {
    const text = this.activeKaynakText;
    if (!text) return;
    navigator.clipboard.writeText(text).then(() => {
      this.copiedToClipboard = true;
      setTimeout(() => this.copiedToClipboard = false, 2500);
    });
  }

  private sanitizeFilename(name: string): string {
    return (name || 'kaynak')
      .replace(/[^a-zA-Z0-9_\-ğüşıöçĞÜŞİÖÇ\.]+/g, '_')
      .substring(0, 60);
  }

  private saveBlob(blob: Blob, filename: string) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  downloadKaynakTxt() {
    const text = this.activeKaynakText;
    if (!text) return;
    const tabName = this.kaynakActiveTab === 'cevirisi' ? 'Turkce_Ceviri' : (this.kaynakActiveTab === 'ozet' ? 'Damitilmis_Ozet' : 'Ham_Metin');
    const filename = `${this.sanitizeFilename(this.kaynakDetay.baslik)}_${tabName}.txt`;
    const blob = new Blob(['\ufeff' + text], { type: 'text/plain;charset=utf-8' });
    this.saveBlob(blob, filename);
    this.isDownloadMenuOpen = false;
  }

  downloadKaynakMd() {
    const text = this.activeKaynakText;
    if (!text) return;
    const tabName = this.kaynakActiveTab === 'cevirisi' ? 'Turkce_Ceviri' : (this.kaynakActiveTab === 'ozet' ? 'Damitilmis_Ozet' : 'Ham_Metin');
    const filename = `${this.sanitizeFilename(this.kaynakDetay.baslik)}_${tabName}.md`;
    const header = `# ${this.kaynakDetay.baslik}\n**Dil:** ${this.kaynakDetay.orijinalDil?.toUpperCase()} | **Tür:** ${this.isArgumanMode(this.kaynakDetay) ? 'Dış Kaynak' : 'Doğrudan Referans'}\n\n---\n\n`;
    const blob = new Blob([header + text], { type: 'text/markdown;charset=utf-8' });
    this.saveBlob(blob, filename);
    this.isDownloadMenuOpen = false;
  }

  downloadKaynakDocx() {
    if (!this.kaynakDetay || this.isGeneratingDocx) return;
    this.isGeneratingDocx = true;
    const tabName = this.kaynakActiveTab === 'cevirisi' ? 'Türkçe Çeviri (Terim Korumalı)' : (this.kaynakActiveTab === 'ozet' ? 'Damıtılmış Bilgi Özeti' : 'Ham Metin / Transkript');
    const filename = `${this.sanitizeFilename(this.kaynakDetay.baslik)}_${this.kaynakActiveTab}.docx`;

    const sections: DocxSection[] = [
      { heading: tabName, markdown: this.activeKaynakText }
    ];

    markdownToDocxBlob({
      title: this.kaynakDetay.baslik,
      subtitle: `${tabName} · Dil: ${this.kaynakDetay.orijinalDil?.toUpperCase() || 'TR'}`,
      sections
    })
      .then(blob => this.saveBlob(blob, filename))
      .catch(err => {
        console.error('Docx hatası:', err);
        alert('Word dosyası oluşturulamadı.');
      })
      .finally(() => {
        this.isGeneratingDocx = false;
        this.isDownloadMenuOpen = false;
      });
  }

  downloadKaynakPdf() {
    if (!this.kaynakDetay) return;
    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      alert('Açılır pencere engellendi. Lütfen tarayıcınızda açılır pencerelere izin verin.');
      return;
    }
    const tabName = this.kaynakActiveTab === 'cevirisi' ? 'Türkçe Çeviri (Terim Korumalı)' : (this.kaynakActiveTab === 'ozet' ? 'Damıtılmış Bilgi Özeti' : 'Ham Transkript / Metin');
    const htmlContent = `
      <!DOCTYPE html>
      <html>
      <head>
        <meta charset="utf-8">
        <title>${this.escapeHtml(this.kaynakDetay.baslik)} - ${tabName}</title>
        <style>
          body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; line-height: 1.6; color: #1e293b; padding: 40px; }
          h1 { color: #0f172a; border-bottom: 2px solid #e2e8f0; padding-bottom: 8px; font-size: 22px; }
          .meta { color: #64748b; font-size: 13px; margin-bottom: 24px; }
          .content { font-size: 14px; white-space: pre-wrap; word-wrap: break-word; }
        </style>
      </head>
      <body>
        <h1>${this.escapeHtml(this.kaynakDetay.baslik)}</h1>
        <div class="meta">Bölüm: <strong>${tabName}</strong> | Dil: <strong>${this.escapeHtml(this.kaynakDetay.orijinalDil?.toUpperCase())}</strong> | Kelime Sayısı: <strong>${this.kaynakDetay.kelimeSayisi || 0}</strong></div>
        <div class="content">${this.escapeHtml(this.activeKaynakText)}</div>
      </body>
      </html>
    `;
    printWindow.document.open();
    printWindow.document.write(htmlContent);
    printWindow.document.close();
    printWindow.focus();
    setTimeout(() => printWindow.print(), 400);
    this.isDownloadMenuOpen = false;
  }

  private escapeHtml(text: string): string {
    return (text || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }
}
