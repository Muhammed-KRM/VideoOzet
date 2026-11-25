import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/services/api.service';
import {
  ImportRemoteVideosResponseDto,
  RemoteImportItemRequestDto,
  RemoteVideoMetadataDto,
  UzaktanAlimModuValue
} from '../../core/models/remote-import.models';

@Component({
  selector: 'app-url-import',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './url-import.component.html'
})
export class UrlImportComponent {
  @Input() egitimId!: string;
  @Input() kullanimTuru: number = 0;
  @Input() indekslemeTuru: number = 0;
  @Input() mode: 'youtube-link' | 'direct-url' = 'direct-url';
  @Output() importComplete = new EventEmitter<void>();

  private readonly apiService = inject(ApiService);

  urlInput = '';
  bearerToken = '';
  showToken = false;

  isInspecting = false;
  hasInspected = false;
  inspectErrors: string[] = [];
  previewItems: RemoteVideoMetadataDto[] = [];
  selectedUrls = new Set<string>();

  selectedAlimModu: UzaktanAlimModuValue = 0;

  isImporting = false;
  importFeedback = '';
  importWarning = '';
  importError = '';

  get selectedItems(): RemoteVideoMetadataDto[] {
    return this.previewItems.filter((item) => this.isItemSelected(item));
  }

  get selectedCount(): number {
    return this.selectedItems.length;
  }

  get selectableCount(): number {
    return this.previewItems.filter((item) => !this.isItemBlocked(item)).length;
  }

  inspect(): void {
    const rawUrl = this.urlInput.trim();
    if (!rawUrl || this.isInspecting || !this.egitimId) return;

    this.isInspecting = true;
    this.hasInspected = true;
    this.inspectErrors = [];
    this.previewItems = [];
    this.selectedUrls.clear();
    this.importFeedback = '';
    this.importWarning = '';
    this.importError = '';

    const token = this.mode === 'direct-url' && this.bearerToken.trim()
      ? this.bearerToken.trim()
      : null;

    this.apiService
      .inspectRemoteUrl(this.egitimId, {
        url: rawUrl,
        bearerToken: token
      })
      .subscribe({
        next: (res) => {
          this.isInspecting = false;
          this.previewItems = res?.items ?? [];
          this.inspectErrors = res?.errors ?? [];

          for (const item of this.previewItems) {
            if (!this.isItemBlocked(item) && !item.zatenEgitimdeVarMi) {
              this.selectedUrls.add(this.getItemKey(item));
            }
          }
        },
        error: (err: HttpErrorResponse) => {
          this.isInspecting = false;
          this.inspectErrors = [
            this.extractErrorMessage(err, 'Bağlantılar önizlenirken bir hata oluştu.')
          ];
        }
      });
  }

  isItemBlocked(item: RemoteVideoMetadataDto): boolean {
    return !!item.drmKorumaliMi || !!(item.hataMesaji && item.hataMesaji.trim());
  }

  isItemSelected(item: RemoteVideoMetadataDto): boolean {
    if (this.isItemBlocked(item)) return false;
    return this.selectedUrls.has(this.getItemKey(item));
  }

  toggleItem(item: RemoteVideoMetadataDto): void {
    if (this.isItemBlocked(item)) return;
    const key = this.getItemKey(item);
    if (this.selectedUrls.has(key)) {
      this.selectedUrls.delete(key);
    } else {
      this.selectedUrls.add(key);
    }
  }

  toggleSelectAll(): void {
    if (this.selectedCount === this.selectableCount && this.selectableCount > 0) {
      this.selectedUrls.clear();
      return;
    }
    this.selectedUrls.clear();
    for (const item of this.previewItems) {
      if (!this.isItemBlocked(item)) {
        this.selectedUrls.add(this.getItemKey(item));
      }
    }
  }

  importSelected(): void {
    const chosen = this.selectedItems;
    if (chosen.length === 0 || this.isImporting || !this.egitimId) return;

    this.isImporting = true;
    this.importFeedback = '';
    this.importWarning = '';
    this.importError = '';

    const payloadItems: RemoteImportItemRequestDto[] = chosen.map((item) => ({
      kaynakUrl: item.kaynakUrl,
      baslik: item.baslik,
      disKaynakId: item.disKaynakId,
      kaynakPlatform: item.kaynakPlatform,
      kanalAdi: item.kanalAdi ?? null,
      thumbnailUrl: item.thumbnailUrl ?? null,
      sureSaniye: item.sureSaniye ?? null,
      transcriptUrl: item.transcriptUrl ?? null
    }));

    const token = this.mode === 'direct-url' && this.bearerToken.trim()
      ? this.bearerToken.trim()
      : null;

    this.apiService
      .importRemoteVideos(this.egitimId, {
        items: payloadItems,
        alimModu: this.selectedAlimModu,
        kullanimTuru: this.kullanimTuru,
        indekslemeTuru: this.indekslemeTuru,
        tercihEdilenDil: 'tr',
        bearerToken: token
      })
      .subscribe({
        next: (res) => {
          this.isImporting = false;
          const addedCount = res?.eklenenVideolar?.length ?? 0;
          const dupIds = res?.atlananDuplicateIdler ?? [];
          const errors = res?.hatalar ?? [];

          if (addedCount > 0) {
            this.importFeedback =
              res.mesaj || `${addedCount} içerik kuyruğa eklendi.`;
            for (const item of chosen) {
              const id = item.disKaynakId || item.kaynakUrl;
              if (!dupIds.includes(id)) {
                item.zatenEgitimdeVarMi = true;
                this.selectedUrls.delete(this.getItemKey(item));
              }
            }
            this.importComplete.emit();
          }

          if (dupIds.length > 0) {
            this.importWarning = `${dupIds.length} mükerrer içerik zaten eğitimde olduğu için atlandı.`;
          }

          if (errors.length > 0) {
            this.importError = errors.join(' | ');
          }
        },
        error: (err: HttpErrorResponse) => {
          this.isImporting = false;
          if (err.status === 409 && err.error) {
            const conflict = err.error as ImportRemoteVideosResponseDto;
            this.importWarning =
              conflict.mesaj ||
              'Seçilen içeriklerin tamamı zaten bu eğitimde mevcut.';
            return;
          }
          this.importError = this.extractErrorMessage(err, 'İçe aktarma sırasında bir hata oluştu.');
        }
      });
  }

  formatDuration(seconds?: number | null): string {
    if (seconds == null || seconds <= 0) return '';
    const hrs = Math.floor(seconds / 3600);
    const mins = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    if (hrs > 0) {
      return `${hrs}:${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
    }
    return `${mins}:${String(secs).padStart(2, '0')}`;
  }

  isDrmErrorText(msg: string): boolean {
    const lower = (msg || '').toLowerCase();
    return lower.includes('drm') || lower.includes('widevine') || lower.includes('fairplay') || lower.includes('playready');
  }

  isSafeImageUrl(url?: string | null): boolean {
    if (!url) return false;
    return url.startsWith('https://') || url.startsWith('http://');
  }

  platformLabel(platform: RemoteVideoMetadataDto['kaynakPlatform']): string {
    if (platform === 'YouTube' || platform === 1) return 'YouTube';
    if (platform === 'CustomApiManifest' || platform === 3) return 'Kurs Manifesti';
    if (platform === 'DirectUrl' || platform === 2) return 'Direct URL';
    return 'Yerel';
  }

  private getItemKey(item: RemoteVideoMetadataDto): string {
    return item.disKaynakId || item.kaynakUrl;
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
