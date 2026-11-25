import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/services/api.service';
import {
  ImportRemoteVideosResponseDto,
  RemoteImportItemRequestDto,
  UzaktanAlimModuValue,
  YouTubeDurationFilter,
  YouTubeSearchItemDto,
  YouTubeVideoDetailDto
} from '../../core/models/remote-import.models';

const YOUTUBE_ID_REGEX = /^[a-zA-Z0-9_-]{11}$/;

@Component({
  selector: 'app-youtube-browser',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './youtube-browser.component.html'
})
export class YoutubeBrowserComponent {
  @Input() egitimId!: string;
  @Input() kullanimTuru: number = 0;
  @Input() indekslemeTuru: number = 0;
  @Output() kullanimTuruChange = new EventEmitter<number>();
  @Output() indekslemeTuruChange = new EventEmitter<number>();
  @Output() importComplete = new EventEmitter<void>();

  private readonly apiService = inject(ApiService);
  private readonly sanitizer = inject(DomSanitizer);

  searchQuery = '';
  durationFilter: YouTubeDurationFilter = 'any';
  isSearching = false;
  hasSearched = false;
  searchError = '';

  results: YouTubeSearchItemDto[] = [];
  kullanilanKaynak = '';
  kotaAsildiMi = false;
  uyariMesaji = '';

  selectedMap = new Map<string, YouTubeSearchItemDto>();
  selectedAlimModu: UzaktanAlimModuValue = 0;

  // Preview Modal state
  previewItem: YouTubeSearchItemDto | null = null;
  previewDetail: YouTubeVideoDetailDto | null = null;
  previewEmbedUrl: SafeResourceUrl | null = null;
  isLoadingDetail = false;
  detailError = '';

  // Import state
  isImporting = false;
  importFeedback = '';
  importWarning = '';
  importError = '';

  get selectedVideos(): YouTubeSearchItemDto[] {
    return Array.from(this.selectedMap.values());
  }

  get selectedCount(): number {
    return this.selectedMap.size;
  }

  search(): void {
    const q = this.searchQuery.trim();
    if (!q || this.isSearching) return;

    this.isSearching = true;
    this.hasSearched = true;
    this.searchError = '';
    this.importFeedback = '';
    this.importWarning = '';
    this.importError = '';

    this.apiService.searchYouTube(q, 15, this.durationFilter).subscribe({
      next: (res) => {
        this.results = res?.items ?? [];
        this.kullanilanKaynak = res?.kullanilanKaynak ?? 'yt-dlp';
        this.kotaAsildiMi = !!res?.kotaAsildiMi;
        this.uyariMesaji = res?.uyariMesaji ?? '';
        this.isSearching = false;
      },
      error: (err: HttpErrorResponse) => {
        this.isSearching = false;
        this.results = [];
        this.searchError = this.extractErrorMessage(err, 'YouTube araması sırasında bir hata oluştu.');
      }
    });
  }

  setDurationFilter(duration: YouTubeDurationFilter): void {
    if (this.durationFilter === duration) return;
    this.durationFilter = duration;
    if (this.searchQuery.trim() && this.hasSearched) {
      this.search();
    }
  }

  isSelected(videoId: string): boolean {
    return this.selectedMap.has(videoId);
  }

  toggleSelect(item: YouTubeSearchItemDto, event?: Event): void {
    if (event) {
      event.stopPropagation();
    }
    if (!item?.videoId) return;

    if (this.selectedMap.has(item.videoId)) {
      this.selectedMap.delete(item.videoId);
    } else {
      this.selectedMap.set(item.videoId, item);
    }
  }

  clearSelection(): void {
    this.selectedMap.clear();
  }

  setKullanimTuru(val: number): void {
    this.kullanimTuru = val;
    this.kullanimTuruChange.emit(val);
  }

  setIndekslemeTuru(val: number): void {
    this.indekslemeTuru = val;
    this.indekslemeTuruChange.emit(val);
  }

  openPreview(item: YouTubeSearchItemDto): void {
    this.previewItem = item;
    this.previewDetail = null;
    this.detailError = '';
    this.previewEmbedUrl = this.buildSafeEmbedUrl(item.videoId);

    if (!YOUTUBE_ID_REGEX.test(item.videoId)) {
      this.detailError = 'Geçersiz YouTube video kimliği.';
      return;
    }

    this.isLoadingDetail = true;
    this.apiService.getYouTubeVideoDetail(item.videoId).subscribe({
      next: (detail) => {
        this.previewDetail = detail;
        this.isLoadingDetail = false;
      },
      error: (err: HttpErrorResponse) => {
        this.isLoadingDetail = false;
        this.detailError = this.extractErrorMessage(err, 'Video detayı yüklenemedi.');
      }
    });
  }

  closePreview(): void {
    this.previewItem = null;
    this.previewDetail = null;
    this.previewEmbedUrl = null;
    this.detailError = '';
    this.isLoadingDetail = false;
  }

  selectRelatedVideo(item: YouTubeSearchItemDto, event: Event): void {
    event.stopPropagation();
    if (!item?.videoId) return;
    if (!this.selectedMap.has(item.videoId)) {
      this.selectedMap.set(item.videoId, item);
    }
  }

  importSingleFromModal(): void {
    if (!this.previewItem || this.isImporting) return;
    const item: YouTubeSearchItemDto = this.previewDetail
      ? {
          videoId: this.previewDetail.videoId,
          baslik: this.previewDetail.baslik || this.previewItem.baslik,
          kanalAdi: this.previewDetail.kanalAdi || this.previewItem.kanalAdi,
          kanalId: this.previewDetail.kanalId ?? this.previewItem.kanalId,
          thumbnailUrl: this.previewDetail.thumbnailUrl || this.previewItem.thumbnailUrl,
          sureSaniye: this.previewDetail.sureSaniye ?? this.previewItem.sureSaniye,
          sureMetni: this.previewDetail.sureMetni || this.previewItem.sureMetni,
          yayinTarihi: this.previewDetail.yayinTarihi ?? this.previewItem.yayinTarihi,
          goruntulenmeSayisi: this.previewDetail.goruntulenmeSayisi ?? this.previewItem.goruntulenmeSayisi,
          aciklama: this.previewDetail.aciklama ?? this.previewItem.aciklama,
          kaynakUrl: this.previewDetail.kaynakUrl || this.previewItem.kaynakUrl
        }
      : this.previewItem;

    this.executeImport([item], true);
  }

  importSelected(): void {
    if (this.selectedCount === 0 || this.isImporting) return;
    this.executeImport(this.selectedVideos, false);
  }

  private executeImport(items: YouTubeSearchItemDto[], fromModal: boolean): void {
    if (!this.egitimId) {
      this.importError = 'Eğitim kimliği bulunamadı.';
      return;
    }

    this.isImporting = true;
    this.importFeedback = '';
    this.importWarning = '';
    this.importError = '';

    const payloadItems: RemoteImportItemRequestDto[] = items.map((item) => ({
      kaynakUrl: item.kaynakUrl || `https://www.youtube.com/watch?v=${item.videoId}`,
      baslik: item.baslik,
      disKaynakId: item.videoId,
      kaynakPlatform: 1,
      kanalAdi: item.kanalAdi,
      thumbnailUrl: item.thumbnailUrl,
      sureSaniye: item.sureSaniye ?? null
    }));

    this.apiService
      .importRemoteVideos(this.egitimId, {
        items: payloadItems,
        alimModu: this.selectedAlimModu,
        kullanimTuru: this.kullanimTuru,
        indekslemeTuru: this.indekslemeTuru,
        tercihEdilenDil: 'tr'
      })
      .subscribe({
        next: (res) => {
          this.isImporting = false;
          this.handleImportResponse(res, items, fromModal);
        },
        error: (err: HttpErrorResponse) => {
          this.isImporting = false;
          if (err.status === 409 && err.error) {
            const conflictBody = err.error as ImportRemoteVideosResponseDto;
            const dupCount = conflictBody.atlananDuplicateIdler?.length ?? items.length;
            this.importWarning =
              conflictBody.mesaj ||
              `${dupCount} video zaten bu eğitimde mevcut olduğu için atlandı.`;
            if (fromModal) {
              this.closePreview();
            }
            return;
          }
          this.importError = this.extractErrorMessage(err, 'Videolar eğitime eklenirken bir hata oluştu.');
        }
      });
  }

  private handleImportResponse(
    res: ImportRemoteVideosResponseDto,
    submitted: YouTubeSearchItemDto[],
    fromModal: boolean
  ): void {
    const addedCount = res?.eklenenVideolar?.length ?? 0;
    const dupIds = res?.atlananDuplicateIdler ?? [];
    const errors = res?.hatalar ?? [];

    if (addedCount > 0) {
      this.importFeedback =
        res.mesaj || `${addedCount} video işlenmek üzere kuyruğa eklendi.`;
      for (const item of submitted) {
        this.selectedMap.delete(item.videoId);
      }
      this.importComplete.emit();
    }

    if (dupIds.length > 0) {
      this.importWarning = `${dupIds.length} mükerrer video zaten eğitimde bulunduğu için atlandı (${dupIds.join(', ')}).`;
    }

    if (errors.length > 0) {
      this.importError = errors.join(' | ');
    }

    if (fromModal && addedCount > 0) {
      this.closePreview();
    }
  }

  private buildSafeEmbedUrl(videoId: string): SafeResourceUrl | null {
    if (!videoId || !YOUTUBE_ID_REGEX.test(videoId)) {
      return null;
    }
    const url = `https://www.youtube-nocookie.com/embed/${videoId}?rel=0`;
    return this.sanitizer.bypassSecurityTrustResourceUrl(url);
  }

  formatViewCount(count?: number | null): string {
    if (count == null || count < 0) return '';
    if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M görüntülenme`;
    if (count >= 1_000) return `${(count / 1_000).toFixed(1)}B görüntülenme`;
    return `${count} görüntülenme`;
  }

  isSafeImageUrl(url?: string | null): boolean {
    if (!url) return false;
    return url.startsWith('https://') || url.startsWith('http://');
  }

  private extractErrorMessage(err: HttpErrorResponse, fallback: string): string {
    if (typeof err?.error === 'string' && err.error.trim()) {
      return err.error;
    }
    if (typeof err?.error?.detail === 'string' && err.error.detail.trim()) {
      return err.error.detail;
    }
    if (typeof err?.error?.Detail === 'string' && err.error.Detail.trim()) {
      return err.error.Detail;
    }
    if (err?.error?.message) {
      return err.error.message;
    }
    if (err?.error?.mesaj) {
      return err.error.mesaj;
    }
    return err?.message || fallback;
  }
}
