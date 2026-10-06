import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { marked } from 'marked';
import { markdownToDocxBlob, qcToMarkdown, DocxSection } from '../../core/utils/markdown-docx';

type DownloadScope = 'active' | 'all';

@Component({
  selector: 'app-plan-viewer',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './plan-viewer.component.html'
})
export class PlanViewerComponent implements OnChanges {
  @Input() revision: any;
  @Input() title = '';
  @Input() bolumNo: number | null = null;

  activeTab: 'ozet' | 'plan' | 'qc' = 'plan';
  qcRaporList: any[] = [];
  formattedOzet: string = '';
  formattedPlan: string = '';

  isDownloadMenuOpen = false;
  isGeneratingDocx = false;

  ngOnChanges(changes: SimpleChanges) {
    if (changes['revision'] && this.revision) {
      this.parseQcReport();
      this.formattedOzet = this.formatMarkdown(this.revision.arastirmaOzeti || '');
      this.formattedPlan = this.formatMarkdown(this.revision.videoPlani || '');
    }
  }

  parseQcReport() {
    this.qcRaporList = [];
    if (this.revision?.detayliRapor) {
      try {
        const parsed = JSON.parse(this.revision.detayliRapor);
        this.qcRaporList = Array.isArray(parsed) ? parsed : [];
      } catch {
        // Hata durumunda liste bos kalir
      }
    }
  }

  // ───────────────────────── İndirme ─────────────────────────

  toggleDownloadMenu() {
    this.isDownloadMenuOpen = !this.isDownloadMenuOpen;
  }

  get activeTabLabel(): string {
    if (this.activeTab === 'ozet') return 'Araştırma Özeti';
    if (this.activeTab === 'plan') return 'Video Planı';
    return 'Kalite Kontrol';
  }

  private get docTitle(): string {
    const bolum = this.bolumNo ?? this.revision?.bolumNo;
    const baslik = (this.title || '').trim();
    if (bolum && baslik) return `Bölüm ${bolum}: ${baslik}`;
    if (baslik) return baslik;
    if (bolum) return `Bölüm ${bolum}`;
    return 'Video Planı';
  }

  private get versionNo(): number {
    return this.revision?.revizyonNo ?? this.revision?.versiyonNo ?? 1;
  }

  private fileName(scope: DownloadScope, ext: string): string {
    const bolum = this.bolumNo ?? this.revision?.bolumNo;
    const base = (this.title || 'Bolum')
      .replace(/[\\/:*?"<>|'.,;]/g, '')
      .trim()
      .replace(/\s+/g, '_')
      .slice(0, 40) || 'Bolum';
    const suffix = scope === 'all'
      ? 'Tam_Rapor'
      : (this.activeTab === 'ozet' ? 'Arastirma_Ozeti' : (this.activeTab === 'plan' ? 'Video_Plani' : 'QC_Raporu'));
    const prefix = bolum ? `Bolum${bolum}_` : '';
    return `${prefix}${base}_${suffix}_V${this.versionNo}.${ext}`;
  }

  private buildMarkdown(scope: DownloadScope): string {
    const ozet = this.revision?.arastirmaOzeti || '';
    const plan = this.revision?.videoPlani || '';
    const qc = this.buildQcText();

    if (scope === 'all') {
      return `# ${this.docTitle} (Revizyon ${this.versionNo})\n\n` +
        `## 1. Video Planı\n\n${plan || '_Video planı bulunamadı._'}\n\n---\n\n` +
        `## 2. Araştırma Özeti\n\n${ozet || '_Araştırma özeti bulunamadı._'}\n\n---\n\n` +
        `## 3. Kalite Kontrol Raporu\n\n${qc}\n`;
    }
    if (this.activeTab === 'ozet') return ozet;
    if (this.activeTab === 'plan') return plan;
    return `# Kalite Kontrol Raporu\n\n${qc}`;
  }

  private buildQcText(): string {
    const r = this.revision || {};
    let text = `Doğruluk Skoru: %${r.guvenSkorYuzde ?? 0}\n` +
      `Desteklenen: ${r.desteklenenSayisi ?? 0} | Belirsiz: ${r.belirsizSayisi ?? 0} | Desteklenmeyen: ${r.desteklenmeyenSayisi ?? 0}\n\n`;
    if (this.qcRaporList.length === 0) {
      text += 'Detaylı QC raporu bulunamadı.\n';
    } else {
      this.qcRaporList.forEach((item: any, i: number) => {
        text += `${i + 1}. [${(item.durum || '').toUpperCase()}] ${item.iddia || ''}\n   ${item.aciklama || ''}\n\n`;
      });
    }
    return text;
  }

  private markdownToPlainText(md: string): string {
    return md
      .replace(/^#{1,6}\s*/gm, '')
      .replace(/\*\*(.*?)\*\*/g, '$1')
      .replace(/__(.*?)__/g, '$1')
      .replace(/(^|[^*])\*(?!\s)(.*?)\*/g, '$1$2')
      .replace(/`([^`]*)`/g, '$1')
      .replace(/^\s*[-*]\s+/gm, '• ')
      .replace(/\[(.*?)\]\((.*?)\)/g, '$1 ($2)')
      .replace(/^---+$/gm, '────────────────────────');
  }

  downloadMarkdown(scope: DownloadScope = 'all') {
    if (!this.revision) return;
    const blob = new Blob([this.buildMarkdown(scope)], { type: 'text/markdown;charset=utf-8' });
    this.saveBlob(blob, this.fileName(scope, 'md'));
    this.isDownloadMenuOpen = false;
  }

  downloadTxt(scope: DownloadScope = 'all') {
    if (!this.revision) return;
    const text = this.markdownToPlainText(this.buildMarkdown(scope));
    const blob = new Blob(['\ufeff' + text], { type: 'text/plain;charset=utf-8' });
    this.saveBlob(blob, this.fileName(scope, 'txt'));
    this.isDownloadMenuOpen = false;
  }

  downloadDocx(scope: DownloadScope = 'all') {
    if (!this.revision || this.isGeneratingDocx) return;
    this.isGeneratingDocx = true;

    const r = this.revision;
    const qcMd = qcToMarkdown(this.qcRaporList, {
      skor: r.guvenSkorYuzde,
      desteklenen: r.desteklenenSayisi,
      belirsiz: r.belirsizSayisi,
      desteklenmeyen: r.desteklenmeyenSayisi
    });

    let sections: DocxSection[];
    if (scope === 'all') {
      sections = [
        { heading: '1. Video Planı', markdown: r.videoPlani || '' },
        { heading: '2. Araştırma Özeti', markdown: r.arastirmaOzeti || '' },
        { heading: '3. Kalite Kontrol Raporu', markdown: qcMd }
      ];
    } else if (this.activeTab === 'plan') {
      sections = [{ heading: 'Video Planı', markdown: r.videoPlani || '' }];
    } else if (this.activeTab === 'ozet') {
      sections = [{ heading: 'Araştırma Özeti', markdown: r.arastirmaOzeti || '' }];
    } else {
      sections = [{ heading: 'Kalite Kontrol Raporu', markdown: qcMd }];
    }

    const dateStr = new Date().toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' });

    markdownToDocxBlob({
      title: this.docTitle,
      subtitle: `Revizyon ${this.versionNo} · ${dateStr}`,
      sections
    })
      .then((blob) => this.saveBlob(blob, this.fileName(scope, 'docx')))
      .catch((err: any) => {
        console.error('Word (.docx) oluşturulamadı:', err);
        alert('Word dosyası oluşturulamadı. Lütfen tekrar deneyin.');
      })
      .finally(() => {
        this.isGeneratingDocx = false;
        this.isDownloadMenuOpen = false;
      });
  }

  downloadPdf(scope: DownloadScope = 'all') {
    if (!this.revision) return;
    const html = this.buildDocumentHtml(scope, 'print');
    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      alert('Açılır pencere engellendi. Lütfen tarayıcınızda bu site için açılır pencerelere izin verin.');
      return;
    }
    printWindow.document.open();
    printWindow.document.write(html);
    printWindow.document.close();
    printWindow.focus();
    setTimeout(() => printWindow.print(), 400);
    this.isDownloadMenuOpen = false;
  }

  private mdToHtml(md: string): string {
    return md ? (marked.parse(md) as string) : '';
  }

  private escapeHtml(text: string): string {
    return (text || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  private buildQcHtml(): string {
    const r = this.revision || {};
    let html = `
      <table class="meta-table">
        <tr><td class="meta-label">Doğruluk Skoru</td><td>%${r.guvenSkorYuzde ?? 0}</td></tr>
        <tr><td class="meta-label">Desteklenen</td><td>${r.desteklenenSayisi ?? 0}</td></tr>
        <tr><td class="meta-label">Belirsiz</td><td>${r.belirsizSayisi ?? 0}</td></tr>
        <tr><td class="meta-label">Desteklenmeyen</td><td>${r.desteklenmeyenSayisi ?? 0}</td></tr>
      </table>`;

    if (this.qcRaporList.length === 0) {
      return html + '<p>Detaylı QC raporu bulunamadı.</p>';
    }

    html += `<table class="qc-table"><tr><th style="width:30px">#</th><th>İddia</th><th style="width:100px">Durum</th><th>Açıklama</th></tr>`;
    this.qcRaporList.forEach((item: any, i: number) => {
      html += `<tr>
        <td>${i + 1}</td>
        <td>${this.escapeHtml(item.iddia)}</td>
        <td><b>${this.escapeHtml(item.durum)}</b></td>
        <td>${this.escapeHtml(item.aciklama)}</td>
      </tr>`;
    });
    return html + '</table>';
  }

  private buildDocumentHtml(scope: DownloadScope, mode: 'word' | 'print'): string {
    const planHtml = this.mdToHtml(this.revision?.videoPlani || '') || '<p>Video planı bulunamadı.</p>';
    const ozetHtml = this.mdToHtml(this.revision?.arastirmaOzeti || '') || '<p>Araştırma özeti bulunamadı.</p>';
    const pageBreak = '<br style="page-break-before: always; clear: both;" />';

    let body = '';
    if (scope === 'all') {
      body = `
        <h2 class="section-title">1. Video Planı</h2>${planHtml}
        ${pageBreak}
        <h2 class="section-title">2. Araştırma Özeti</h2>${ozetHtml}
        ${pageBreak}
        <h2 class="section-title">3. Kalite Kontrol Raporu</h2>${this.buildQcHtml()}`;
    } else if (this.activeTab === 'plan') {
      body = `<h2 class="section-title">Video Planı</h2>${planHtml}`;
    } else if (this.activeTab === 'ozet') {
      body = `<h2 class="section-title">Araştırma Özeti</h2>${ozetHtml}`;
    } else {
      body = `<h2 class="section-title">Kalite Kontrol Raporu</h2>${this.buildQcHtml()}`;
    }

    const dateStr = new Date().toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' });

    return `<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>${this.escapeHtml(this.docTitle)}</title>
  <style>
    @page { size: A4; margin: 15mm 18mm; }
    body { font-family: Calibri, 'Segoe UI', Arial, sans-serif; font-size: 11pt; line-height: 1.5; color: #1e293b; padding: ${mode === 'print' ? '10px' : '0'}; }
    h1.doc-title { font-size: 20pt; color: #1e3a8a; margin: 0 0 4px 0; }
    p.doc-sub { font-size: 9.5pt; color: #64748b; margin: 0 0 14px 0; }
    h2.section-title { font-size: 15pt; color: #1e3a8a; border-bottom: 1.5px solid #cbd5e1; padding-bottom: 4px; margin-top: 18px; }
    h1 { font-size: 16pt; color: #0f172a; }
    h2 { font-size: 14pt; color: #0f172a; }
    h3 { font-size: 12pt; color: #1e40af; }
    li { margin: 2px 0; }
    hr { border: none; border-top: 1px solid #cbd5e1; margin: 12px 0; }
    table { border-collapse: collapse; width: 100%; margin: 8px 0 14px 0; }
    td, th { border: 1px solid #cbd5e1; padding: 5px 8px; font-size: 10pt; vertical-align: top; text-align: left; }
    th { background: #e2e8f0; }
    .meta-label { font-weight: bold; background: #f1f5f9; width: 160px; }
  </style>
</head>
<body>
  <h1 class="doc-title">${this.escapeHtml(this.docTitle)}</h1>
  <p class="doc-sub">Revizyon ${this.versionNo} · ${dateStr}</p>
  ${body}
</body>
</html>`;
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

  private formatMarkdown(text: string): string {
    if (!text) return '';
    let html = text
      .replace(/### (.*)/g, '<h3 class="text-lg font-bold text-white mt-4 mb-2">$1</h3>')
      .replace(/## (.*)/g, '<h2 class="text-xl font-bold text-white mt-5 mb-3">$1</h2>')
      .replace(/# (.*)/g, '<h1 class="text-2xl font-bold text-white mt-6 mb-4">$1</h1>')
      .replace(/\*\*(.*?)\*\*/g, '<strong class="text-indigo-300 font-bold">$1</strong>')
      .replace(/\*(.*?)\*/g, '<em class="text-slate-300 italic">$1</em>')
      .replace(/- (.*)/g, '<li class="ml-4 list-disc text-slate-300 my-1">$1</li>')
      .replace(/\n\n/g, '<br><br>');
    return html;
  }
}
