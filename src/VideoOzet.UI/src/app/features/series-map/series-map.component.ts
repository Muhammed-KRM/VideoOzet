import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';

/**
 * Seri haritası.
 * Kaynak: API'nin döndüğü yapılandırılmış `plan.seriBolumler` listesi (DB'deki SeriBolumler tablosu).
 * Not: `seriHaritasiJson` LLM'in ham çıktısıdır ve şekli garanti değildir; görüntüleme için kullanılmaz.
 * Enum alanları (durum) API'den STRING gelir: "Bekliyor" | "Isleniyor" | "Tamamlandi" | "Hata".
 */
@Component({
  selector: 'app-series-map',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './series-map.component.html'
})
export class SeriesMapComponent {
  @Input() plan: any = null;
  @Input() readonly: boolean = false;
  @Input() contentRequestId: string = '';

  constructor(private router: Router) {}

  // Getter olarak tutulur: polling ile gelen her yeni `plan` referansında otomatik güncellenir.
  get bolumler(): any[] {
    return [...(this.plan?.seriBolumler || [])].sort((a: any, b: any) => a.bolumNo - b.bolumNo);
  }

  getKonular(b: any): string[] {
    if (!b?.konularJson) return [];
    try {
      const parsed = JSON.parse(b.konularJson);
      return Array.isArray(parsed) ? parsed.map((k: any) => (typeof k === 'string' ? k : JSON.stringify(k))) : [];
    } catch {
      return [];
    }
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
    return b.id;
  }

  goToEpisode(bolumNo: number) {
    if (!this.plan?.onaylandi) return;
    this.router.navigate(['/episode-viewer', this.contentRequestId, this.plan.planNo, bolumNo]);
  }
}
