import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { SeriesMapComponent } from '../series-map/series-map.component';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-series-planner',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, SeriesMapComponent],
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
  currentStatusMessage: string = '';

  // Taslak Form
  draftOptions = {
    videoSayisi: null,
    varsayilanSureDk: null,
    talimat: ''
  };
  isDrafting = false;
  onayTalimati = '';
  isApproving = false;

  private subs: Subscription[] = [];

  ngOnInit() {
    this.contentRequestId = this.route.snapshot.paramMap.get('id') || '';
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

        // NOT: API tüm enum'ları global JsonStringEnumConverter ile STRING olarak döner
        // ("Bekliyor", "Olusturuluyor", "OnayBekliyor" ...). Sayısal karşılaştırma asla eşleşmez.
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
        // Polling'i ASLA durdurmuyoruz! Geçici ağ veya sunucu yanıt gecikmelerinde
        // polling kesilirse kullanıcı ekranda takılı kalır.
      }
    });
  }

  /** Arka planda hâlâ bir işlem (analiz / plan üretimi / bölüm üretimi) sürüyor mu? */
  isStillProcessing(): boolean {
    const d = this.data;
    if (!d) return true;

    // Kullanıcı yeni bir taslak talep ettiyse
    if (this.isDrafting) return true;

    // Genel istek durumu devam ediyorsa
    if (d.durum === 'Bekliyor' || d.durum === 'IcerikUretiliyor' || d.durum === 'QcYapiliyor') {
      return true;
    }

    const analiz = d.konuAnalizi;
    if (!analiz) {
      return d.durum !== 'Hata';
    }
    if (analiz.durum === 'Isleniyor' || analiz.durum === 'Bekliyor') {
      return true;
    }

    const plan = d.guncelPlan;
    if (!plan) {
      // Analiz tamamlanmışsa plan oluşturulması bekleniyor demektir
      return analiz.durum !== 'Hata';
    }

    if (plan.durum === 'Olusturuluyor' || plan.durum === 'Taslak') {
      return true;
    }

    // Plan onaylandıysa bölüm üretimi sürüyor olabilir
    if (plan.onaylandi) {
      const bolumler = plan.seriBolumler || [];
      const anyPendingOrProcessing = bolumler.some(
        (b: any) => b.durum === 'Isleniyor' || b.durum === 'Bekliyor'
      );
      return anyPendingOrProcessing;
    }

    // Plan OnayBekliyor durumunda ve kullanıcı aksiyonu bekleniyor
    return false;
  }

  get isPlanGenerating(): boolean {
    const d = this.data;
    if (!d) return true;
    if (this.isDrafting) return true;

    // Konu analizi henüz tamamlanmadıysa
    if (!d.konuAnalizi || d.konuAnalizi.durum === 'Isleniyor' || d.konuAnalizi.durum === 'Bekliyor') {
      return true;
    }

    // Analiz başarısız olduysa ve plan da yoksa
    if (d.konuAnalizi.durum === 'Hata' && !d.guncelPlan) return false;

    // Plan henüz kaydedilmediyse veya oluşturuluyorsa / hiç bölüm yoksa
    return !d.guncelPlan || d.guncelPlan.durum === 'Olusturuluyor' || d.guncelPlan.durum === 'Taslak' || (d.guncelPlan.seriBolumler?.length ?? 0) === 0;
  }

  get hasError(): boolean {
    const d = this.data;
    if (!d) return false;

    // Eğer güncel planda en az 1 bölüm başarıyla üretilmişse KESİNLİKLE planlama hatası değildir!
    if (d.guncelPlan && (d.guncelPlan.seriBolumler?.length ?? 0) > 0) {
      return false;
    }

    // Eğer bir işlem aktif olarak sürüyorsa hata ekranı açma
    if (this.isDrafting || this.isPlanGenerating || this.isStillProcessing()) {
      return false;
    }

    // Sadece hiçbir geçerli plan yokken ve işlem kalıcı hataya düştüğünde
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

  requestNewDraft() {
    if (!this.data || this.isDrafting) return;
    this.isDrafting = true;
    this.currentStatusMessage = 'Yeni plan taslağı isteniyor...';
    this.apiService.generatePlanDraft(this.contentRequestId, this.draftOptions).subscribe({
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
    
    // Yalnızca yazdırılabilir HTML üretelim ve yeni pencerede açıp yazdıralım.
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
              // Bos bir satir ekle
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
    
    // MD ve TXT için düz metin formatı
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
