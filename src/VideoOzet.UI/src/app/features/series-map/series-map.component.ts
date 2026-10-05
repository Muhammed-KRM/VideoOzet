import { Component, Input, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';

@Component({
  selector: 'app-series-map',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './series-map.component.html'
})
export class SeriesMapComponent implements OnInit {
  @Input() plan: any = null;
  @Input() readonly: boolean = false;
  @Input() contentRequestId: string = '';

  haritaData: any = null;

  constructor(private router: Router) {}

  ngOnInit() {
    if (this.plan?.seriHaritasiJson) {
      try {
        this.haritaData = JSON.parse(this.plan.seriHaritasiJson);
      } catch (e) {
        console.error('Harita JSON parse hatası', e);
      }
    }
  }

  getVideos() {
    return this.haritaData?.Videolar || [];
  }

  getZamanCizelgesi(v: any) {
    if (!v.ZamanCizelgesi) return [];
    return Object.keys(v.ZamanCizelgesi).map(k => ({
      dakika: k,
      aciklama: v.ZamanCizelgesi[k]
    }));
  }

  goToEpisode(bolumNo: number) {
    if (!this.plan.onaylandi) return;
    this.router.navigate(['/episode-viewer', this.contentRequestId, this.plan.planNo, bolumNo]);
  }
}
