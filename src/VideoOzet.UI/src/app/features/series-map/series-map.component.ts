import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import {
  ApiService,
  CourseSourceCardDto,
  SeriesSceneOutlineItemDto,
  UpdateEpisodeItemDto,
  UpdatePlanDto
} from '../../core/services/api.service';

@Component({
  selector: 'app-series-map',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './series-map.component.html'
})
export class SeriesMapComponent {
  @Input() plan: any = null;
  @Input() readonly: boolean = false;
  @Input() contentRequestId: string = '';
  @Input() egitimKaynaklari: CourseSourceCardDto[] = [];
  @Output() planUpdated = new EventEmitter<void>();

  // Bölüm Düzenleme Modalı Durumu
  isEditModalOpen = false;
  isSaving = false;
  saveError = '';
  editingIndex: number = -1;
  isNewEpisode = false;
  sourceSearchQuery = '';

  editForm: {
    bolumNo: number;
    calismaBasligi: string;
    kanca: string;
    hedefSureDk: number;
    anaFikir: string;
    konularText: string;
    sahneIskeleti: SeriesSceneOutlineItemDto[];
    atananKaynakIdleri: string[];
  } = {
    bolumNo: 1,
    calismaBasligi: '',
    kanca: '',
    hedefSureDk: 15,
    anaFikir: '',
    konularText: '',
    sahneIskeleti: [],
    atananKaynakIdleri: []
  };

  constructor(private router: Router, private apiService: ApiService) {}

  get bolumler(): any[] {
    return [...(this.plan?.seriBolumler || [])].sort((a: any, b: any) => a.bolumNo - b.bolumNo);
  }

  getKonular(b: any): string[] {
    if (Array.isArray(b?.konular)) {
      return b.konular.map((k: any) => (typeof k === 'string' ? k : JSON.stringify(k)));
    }
    if (!b?.konularJson) return [];
    try {
      const parsed = JSON.parse(b.konularJson);
      return Array.isArray(parsed) ? parsed.map((k: any) => (typeof k === 'string' ? k : JSON.stringify(k))) : [];
    } catch {
      return [];
    }
  }

  getSahneIskeleti(b: any): SeriesSceneOutlineItemDto[] {
    const raw = b?.sahneIskeleti ?? b?.sahneIskeletiJson;
    if (!raw) return [];
    let arr: any[] = [];
    if (Array.isArray(raw)) {
      arr = raw;
    } else if (typeof raw === 'string' && raw.trim()) {
      try {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) arr = parsed;
      } catch {
        return [];
      }
    }
    return arr.map((s: any, idx: number) => ({
      sahneNo: Number(s?.sahneNo ?? s?.SahneNo ?? idx + 1),
      baslik: String(s?.baslik ?? s?.Baslik ?? `Sahne ${idx + 1}`),
      tahminiSureDk: Number(s?.tahminiSureDk ?? s?.TahminiSureDk ?? 3),
      aciklamaVeArgumanlar: String(s?.aciklamaVeArgumanlar ?? s?.AciklamaVeArgumanlar ?? s?.aciklama ?? ''),
      atananKaynakKodlari: Array.isArray(s?.atananKaynakKodlari ?? s?.AtananKaynakKodlari)
        ? (s.atananKaynakKodlari ?? s.AtananKaynakKodlari)
        : []
    }));
  }

  private static readonly GUID_REGEX =
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

  private isValidGuid(val: string | null | undefined): boolean {
    return !!val && SeriesMapComponent.GUID_REGEX.test(val.trim());
  }

  getAtananKaynakIdleri(b: any): string[] {
    const raw = b?.atananKaynakIdleri ?? b?.atananKaynaklar ?? b?.atananKaynaklarJson;
    if (!raw) return [];
    let arr: any[] = [];
    if (Array.isArray(raw)) {
      arr = raw;
    } else if (typeof raw === 'string' && raw.trim()) {
      try {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) arr = parsed;
      } catch {
        return [];
      }
    }

    const result: string[] = [];
    for (const item of arr) {
      let candidate = '';
      let titleCandidate = '';
      if (typeof item === 'string' && item.trim()) {
        candidate = item.trim();
        titleCandidate = candidate;
      } else if (item && typeof item === 'object') {
        candidate = String(item.kaynakId || item.id || '').trim();
        titleCandidate = String(item.baslik || item.dosyaAdi || '').trim();
      }

      if (this.isValidGuid(candidate)) {
        result.push(candidate);
      } else if (titleCandidate && this.egitimKaynaklari?.length) {
        const matched = this.egitimKaynaklari.find(
          s => (s.baslik || '').trim().toLowerCase() === titleCandidate.toLowerCase()
        );
        if (matched && this.isValidGuid(matched.kaynakId)) {
          result.push(matched.kaynakId);
        } else if (candidate) {
          result.push(candidate);
        }
      } else if (candidate) {
        result.push(candidate);
      }
    }
    return Array.from(new Set(result));
  }

  private getValidGuidList(ids: string[]): string[] {
    const out: string[] = [];
    for (const rawId of ids || []) {
      const trimmed = (rawId || '').trim();
      if (this.isValidGuid(trimmed)) {
        out.push(trimmed);
      } else if (trimmed && this.egitimKaynaklari?.length) {
        const matched = this.egitimKaynaklari.find(
          s => (s.baslik || '').trim().toLowerCase() === trimmed.toLowerCase()
        );
        if (matched && this.isValidGuid(matched.kaynakId)) {
          out.push(matched.kaynakId);
        }
      }
    }
    return Array.from(new Set(out));
  }

  getAtananKaynakDetaylari(b: any): { id: string; baslik: string; kaynakTuru: string }[] {
    const ids = this.getAtananKaynakIdleri(b);
    if (ids.length === 0) return [];

    const sourceMap = new Map<string, CourseSourceCardDto>();
    for (const s of this.egitimKaynaklari || []) {
      if (s.kaynakId) {
        sourceMap.set(s.kaynakId.toLowerCase(), s);
      }
    }

    return ids.map(rawId => {
      const found = sourceMap.get(rawId.toLowerCase());
      if (found) {
        return {
          id: found.kaynakId,
          baslik: found.baslik,
          kaynakTuru: found.kaynakTuru
        };
      }
      return {
        id: rawId,
        baslik: rawId.length > 30 ? `Kaynak (${rawId.slice(0, 8)}...)` : rawId,
        kaynakTuru: 'Kaynak'
      };
    });
  }

  getBaglamSorunlari(b: any): string[] {
    const raw = b?.baglamSorunlariJson ?? b?.baglamSorunlari;
    if (!raw) return [];
    if (Array.isArray(raw)) return raw.map((x: any) => String(x));
    if (typeof raw === 'string' && raw.trim()) {
      try {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) {
          return parsed.map((x: any) => (typeof x === 'string' ? x : (x?.mesaj || x?.aciklama || JSON.stringify(x))));
        }
        if (parsed && typeof parsed === 'object') {
          return [parsed.mesaj || parsed.aciklama || JSON.stringify(parsed)];
        }
      } catch {
        return [raw.trim()];
      }
    }
    return [];
  }

  durumEtiketi(durum: string): string {
    switch (durum) {
      case 'Isleniyor': return 'Üretiliyor...';
      case 'Tamamlandi': return 'Tamamlandı';
      case 'Hata': return 'Hata';
      default: return 'Sırada';
    }
  }

  trackByBolum(_: number, b: any) {
    return b.id || b.bolumNo;
  }

  goToEpisode(bolumNo: number) {
    if (!this.plan?.onaylandi) return;
    this.router.navigate(['/episode-viewer', this.contentRequestId, this.plan.planNo, bolumNo]);
  }

  retryBolum(event: Event, bolumNo: number) {
    event.stopPropagation();
    if (!this.contentRequestId) return;
    const target = this.bolumler.find(b => b.bolumNo === bolumNo);
    if (target) {
      target.durum = 'Isleniyor';
      target.baglamEskidi = false;
    }
    this.apiService.retryEpisode(this.contentRequestId, bolumNo).subscribe({
      next: () => {
        this.planUpdated.emit();
      },
      error: (err) => {
        console.error('Yeniden deneme hatası:', err);
        if (target) {
          target.durum = 'Hata';
        }
      }
    });
  }

  // ── Harita Üzerinde Doğrudan Düzenleme, Ekleme, Silme ve Sıralama (!readonly) ──

  private toUpdateEpisodeDto(b: any, index: number): UpdateEpisodeItemDto {
    return {
      bolumNo: index + 1,
      calismaBasligi: String(b.calismaBasligi || `Bölüm ${index + 1}`).trim(),
      kanca: String(b.kanca || '').trim(),
      anaFikir: String(b.anaFikir || '').trim(),
      hedefSureDk: Math.max(2, Math.round(Number(b.hedefSureDk) || 10)),
      konular: this.getKonular(b),
      sahneIskeleti: this.getSahneIskeleti(b).map((s, sIdx) => ({
        sahneNo: sIdx + 1,
        baslik: String(s.baslik || `Sahne ${sIdx + 1}`).trim(),
        tahminiSureDk: Math.max(1, Math.round(Number(s.tahminiSureDk) || 2)),
        aciklamaVeArgumanlar: String(s.aciklamaVeArgumanlar || '').trim(),
        atananKaynakKodlari: (s.atananKaynakKodlari || []).map(x => String(x || '')).filter(Boolean)
      })),
      atananKaynakIdleri: this.getValidGuidList(this.getAtananKaynakIdleri(b))
    };
  }

  openEditModal(event: Event, index: number) {
    event.stopPropagation();
    if (this.readonly) return;

    const b = this.bolumler[index];
    if (!b) return;

    this.editingIndex = index;
    this.isNewEpisode = false;
    this.saveError = '';
    this.sourceSearchQuery = '';

    const sahneler = this.getSahneIskeleti(b);
    this.editForm = {
      bolumNo: b.bolumNo,
      calismaBasligi: b.calismaBasligi || '',
      kanca: b.kanca || '',
      hedefSureDk: Number(b.hedefSureDk) || 15,
      anaFikir: b.anaFikir || '',
      konularText: this.getKonular(b).join('\n'),
      sahneIskeleti: sahneler.length > 0 ? sahneler.map(s => ({ ...s })) : [],
      atananKaynakIdleri: [...this.getAtananKaynakIdleri(b)]
    };

    this.isEditModalOpen = true;
  }

  openAddEpisodeModal(event?: Event) {
    if (event) event.stopPropagation();
    if (this.readonly) return;

    const nextNo = this.bolumler.length + 1;
    this.editingIndex = this.bolumler.length;
    this.isNewEpisode = true;
    this.saveError = '';
    this.sourceSearchQuery = '';

    this.editForm = {
      bolumNo: nextNo,
      calismaBasligi: `${nextNo}. Bölüm: Yeni Konu Başlığı`,
      kanca: '',
      hedefSureDk: Number(this.plan?.varsayilanVideoSuresiDk) || 15,
      anaFikir: '',
      konularText: '',
      sahneIskeleti: [
        {
          sahneNo: 1,
          baslik: 'Giriş ve Temel Soru',
          tahminiSureDk: 3,
          aciklamaVeArgumanlar: 'Bölümün temel sorusunun ve kavramsal çerçevesinin ortaya konulması.'
        }
      ],
      atananKaynakIdleri: []
    };

    this.isEditModalOpen = true;
  }

  closeEditModal() {
    if (this.isSaving) return;
    this.isEditModalOpen = false;
  }

  addSceneToEditForm() {
    const nextNo = this.editForm.sahneIskeleti.length + 1;
    this.editForm.sahneIskeleti.push({
      sahneNo: nextNo,
      baslik: `Sahne ${nextNo}`,
      tahminiSureDk: 3,
      aciklamaVeArgumanlar: ''
    });
  }

  removeSceneFromEditForm(idx: number) {
    this.editForm.sahneIskeleti.splice(idx, 1);
    this.editForm.sahneIskeleti.forEach((s, i) => (s.sahneNo = i + 1));
  }

  isSourceSelected(kaynakId: string): boolean {
    const lower = (kaynakId || '').toLowerCase();
    return this.editForm.atananKaynakIdleri.some(id => id.toLowerCase() === lower);
  }

  toggleSourceSelection(kaynakId: string) {
    const lower = (kaynakId || '').toLowerCase();
    const existingIdx = this.editForm.atananKaynakIdleri.findIndex(id => id.toLowerCase() === lower);
    if (existingIdx >= 0) {
      this.editForm.atananKaynakIdleri.splice(existingIdx, 1);
    } else {
      this.editForm.atananKaynakIdleri.push(kaynakId);
    }
  }

  get filteredEgitimKaynaklari(): CourseSourceCardDto[] {
    const q = (this.sourceSearchQuery || '').trim().toLowerCase();
    if (!q) return this.egitimKaynaklari || [];
    return (this.egitimKaynaklari || []).filter(
      s =>
        (s.baslik || '').toLowerCase().includes(q) ||
        (s.kaynakTuru || '').toLowerCase().includes(q)
    );
  }

  saveEditModal() {
    if (this.readonly || this.isSaving) return;
    if (!this.editForm.calismaBasligi.trim()) {
      this.saveError = 'Lütfen bölüm için bir çalışma başlığı girin.';
      return;
    }

    const dtoList = this.bolumler.map((b, idx) => this.toUpdateEpisodeDto(b, idx));
    const konular = this.editForm.konularText
      .split(/\r?\n/)
      .map(s => s.trim())
      .filter(Boolean);

    const editedDto: UpdateEpisodeItemDto = {
      bolumNo: this.editingIndex + 1,
      calismaBasligi: this.editForm.calismaBasligi.trim(),
      kanca: this.editForm.kanca.trim(),
      anaFikir: this.editForm.anaFikir.trim(),
      hedefSureDk: Math.max(2, Math.round(Number(this.editForm.hedefSureDk) || 10)),
      konular,
      sahneIskeleti: this.editForm.sahneIskeleti.map((s, sIdx) => ({
        sahneNo: sIdx + 1,
        baslik: String(s.baslik || `Sahne ${sIdx + 1}`).trim(),
        tahminiSureDk: Math.max(1, Math.round(Number(s.tahminiSureDk) || 2)),
        aciklamaVeArgumanlar: String(s.aciklamaVeArgumanlar || '').trim(),
        atananKaynakKodlari: (s.atananKaynakKodlari || []).map(x => String(x || '')).filter(Boolean)
      })),
      atananKaynakIdleri: this.getValidGuidList(this.editForm.atananKaynakIdleri)
    };

    if (this.isNewEpisode) {
      dtoList.push(editedDto);
    } else if (this.editingIndex >= 0 && this.editingIndex < dtoList.length) {
      dtoList[this.editingIndex] = editedDto;
    }

    this.persistPlanDtos(dtoList, true);
  }

  moveEpisode(event: Event, index: number, direction: -1 | 1) {
    event.stopPropagation();
    if (this.readonly || this.isSaving) return;

    const currentList = this.bolumler;
    const targetIndex = index + direction;
    if (targetIndex < 0 || targetIndex >= currentList.length) return;

    const dtoList = currentList.map((b, idx) => this.toUpdateEpisodeDto(b, idx));
    const temp = dtoList[index];
    dtoList[index] = dtoList[targetIndex];
    dtoList[targetIndex] = temp;

    this.persistPlanDtos(dtoList, false);
  }

  deleteEpisode(event: Event, index: number) {
    event.stopPropagation();
    if (this.readonly || this.isSaving) return;

    const currentList = this.bolumler;
    if (currentList.length <= 1) {
      alert('Planda en az 1 bölüm bulunmalıdır.');
      return;
    }

    const target = currentList[index];
    if (!confirm(`"Bölüm ${target.bolumNo}: ${target.calismaBasligi}" bölümünü silmek istediğinize emin misiniz?`)) {
      return;
    }

    const dtoList = currentList
      .filter((_, idx) => idx !== index)
      .map((b, idx) => this.toUpdateEpisodeDto(b, idx));

    this.persistPlanDtos(dtoList, false);
  }

  private persistPlanDtos(dtoList: UpdateEpisodeItemDto[], closeModalOnSuccess: boolean) {
    if (!this.contentRequestId || !this.plan?.planNo) return;

    dtoList.forEach((item, idx) => {
      item.bolumNo = idx + 1;
    });

    const payload: UpdatePlanDto = {
      bolumler: dtoList
    };

    this.isSaving = true;
    this.saveError = '';

    this.apiService.updatePlanTexts(this.contentRequestId, this.plan.planNo, payload).subscribe({
      next: (res: any) => {
        this.isSaving = false;
        // Yerel görünümü anında güncelle
        if (this.plan) {
          if (Array.isArray(res?.seriBolumler)) {
            this.plan.seriBolumler = res.seriBolumler;
          } else {
            this.plan.seriBolumler = dtoList.map((d, idx) => ({
              ...(this.bolumler[idx] || {}),
              bolumNo: d.bolumNo,
              calismaBasligi: d.calismaBasligi,
              kanca: d.kanca,
              anaFikir: d.anaFikir,
              hedefSureDk: d.hedefSureDk,
              konularJson: JSON.stringify(d.konular),
              sahneIskeletiJson: JSON.stringify(d.sahneIskeleti),
              atananKaynaklarJson: JSON.stringify(d.atananKaynakIdleri),
              atananKaynakIdleri: d.atananKaynakIdleri,
              durum: 'Bekliyor'
            }));
          }
        }
        if (closeModalOnSuccess) {
          this.isEditModalOpen = false;
        }
        this.planUpdated.emit();
      },
      error: (err) => {
        console.error('Plan güncelleme hatası:', err);
        this.isSaving = false;
        let validationMsg = '';
        if (err?.error?.errors && typeof err.error.errors === 'object') {
          validationMsg = Object.entries(err.error.errors)
            .map(([k, v]: [string, any]) => `${k}: ${Array.isArray(v) ? v.join(', ') : v}`)
            .join(' | ');
        }
        this.saveError = err?.error?.mesaj || validationMsg || err?.message || 'Plan kaydedilirken bir hata oluştu.';
        if (!closeModalOnSuccess) {
          alert(this.saveError);
        }
      }
    });
  }
}
