import { Component, Input, OnChanges, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { marked } from 'marked';
import { ApiService } from '../../core/services/api.service';

@Component({
  selector: 'app-content-result',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './content-result.component.html'
})
export class ContentResultComponent implements OnChanges {
  @Input() result: any = null;
  @Input() isLoading = false;

  private apiService = inject(ApiService);

  activeTab = 'ozet'; // 'ozet', 'plan', 'qc'
  
  parsedOzet = '';
  parsedPlan = '';
  parsedQcReport: any[] = [];

  // Versiyona Özel Kalite Kontrol (QC) Durumu
  activeQcReport: any[] = [];
  activeQcScore: number = 0;
  activeDesteklenen: number = 0;
  activeBelirsiz: number = 0;
  activeDesteklenmeyen: number = 0;
  activeQcDate: string | null = null;
  hasActiveQc: boolean = false;

  // QC Yeniden Çalıştırma Durumu
  isReQcRunning = false;
  reQcError = '';

  // Versiyon Yönetimi (V1, V2, V3...)
  versions: any[] = [];
  activeVersion: any = null;

  // Revizyon Formu
  isRevisionModalOpen = false;
  isRevising = false;
  revisionInstruction = '';
  revisionTarget = 'hepsi'; // 'hepsi', 'ozet', 'plan'
  revisionError = '';

  isDownloadMenuOpen = false;
  copySuccess = false;

  ngOnChanges() {
    if (this.result) {
      this.initializeVersions();
      this.activeTab = 'ozet';
    }
  }

  initializeVersions() {
    if (this.result.versions && this.result.versions.length > 0) {
      this.versions = [...this.result.versions].sort((a, b) => a.versiyonNo - b.versiyonNo);
    } else if (this.result.generatedContent) {
      // Geriye dönük uyumluluk için tek versiyon üret
      this.versions = [
        {
          id: this.result.generatedContent.id,
          versiyonNo: 1,
          arastirmaOzeti: this.result.generatedContent.arastirmaOzeti,
          videoPlani: this.result.generatedContent.videoPlani,
          revizeTalimati: 'İlk Üretim (Orijinal Versiyon)',
          llmModel: this.result.generatedContent.llmModel,
          olusturmaTarihi: this.result.generatedContent.olusturmaTarihi,
          guvenSkorYuzde: this.result.qcResult?.guvenSkorYuzde ?? this.result.guvenSkorYuzde,
          desteklenenSayisi: this.result.qcResult?.desteklenenSayisi,
          belirsizSayisi: this.result.qcResult?.belirsizSayisi,
          desteklenmeyenSayisi: this.result.qcResult?.desteklenmeyenSayisi,
          detayliRapor: this.result.qcResult?.detayliRapor,
          qcTarihi: this.result.qcResult?.olusturmaTarihi
        }
      ];
    } else {
      this.versions = [];
    }

    // Varsayılan olarak en son versiyonu seç
    if (this.versions.length > 0) {
      const latest = this.versions[this.versions.length - 1];
      this.selectVersion(latest);
    } else {
      const ozet = this.result.generatedContent?.arastirmaOzeti ?? this.result.arastirmaOzeti ?? '';
      const plan = this.result.generatedContent?.videoPlani ?? this.result.videoPlani ?? '';
      this.parsedOzet = ozet ? (marked.parse(ozet) as string) : '';
      this.parsedPlan = plan ? (marked.parse(plan) as string) : '';
      this.activeVersion = null;
      this.updateActiveVersionQc(null);
    }
  }

  selectVersion(version: any) {
    this.activeVersion = version;
    const ozet = version?.arastirmaOzeti ?? '';
    const plan = version?.videoPlani ?? '';
    this.parsedOzet = ozet ? (marked.parse(ozet) as string) : '';
    this.parsedPlan = plan ? (marked.parse(plan) as string) : '';

    this.updateActiveVersionQc(version);
  }

  updateActiveVersionQc(version: any) {
    const rawQc = version?.detayliRapor 
      ?? (version?.versiyonNo === 1 ? (this.result?.qcResult?.detayliRapor ?? this.result?.detayliRapor) : null);

    try {
      this.activeQcReport = typeof rawQc === 'string' ? JSON.parse(rawQc) : (rawQc || []);
    } catch {
      this.activeQcReport = [];
    }

    this.hasActiveQc = this.activeQcReport.length > 0 || version?.guvenSkorYuzde != null;

    this.activeQcScore = version?.guvenSkorYuzde 
      ?? (version?.versiyonNo === 1 ? (this.result?.qcResult?.guvenSkorYuzde ?? this.result?.guvenSkorYuzde ?? 0) : 0);

    this.activeDesteklenen = version?.desteklenenSayisi 
      ?? (version?.versiyonNo === 1 ? (this.result?.qcResult?.desteklenenSayisi ?? 0) : 0);

    this.activeBelirsiz = version?.belirsizSayisi 
      ?? (version?.versiyonNo === 1 ? (this.result?.qcResult?.belirsizSayisi ?? 0) : 0);

    this.activeDesteklenmeyen = version?.desteklenmeyenSayisi 
      ?? (version?.versiyonNo === 1 ? (this.result?.qcResult?.desteklenmeyenSayisi ?? 0) : 0);

    this.activeQcDate = version?.qcTarihi 
      ?? (version?.versiyonNo === 1 ? (this.result?.qcResult?.olusturmaTarihi ?? null) : null);

    this.parsedQcReport = this.activeQcReport;
  }

  reRunQualityCheck() {
    if (!this.result?.id || !this.activeVersion || this.isReQcRunning) return;

    this.isReQcRunning = true;
    this.reQcError = '';

    const verNo = this.activeVersion.versiyonNo;

    this.apiService.reRunQualityCheck(this.result.id, verNo).subscribe({
      next: (updatedResult) => {
        this.result = updatedResult;
        this.initializeVersions();

        // Yeniden kontrol edilen versiyonu seç
        const updatedVersion = this.versions.find(v => v.versiyonNo === verNo);
        if (updatedVersion) {
          this.selectVersion(updatedVersion);
        }
        this.isReQcRunning = false;
      },
      error: (err) => {
        console.error('Kalite kontrol hatası:', err);
        this.isReQcRunning = false;
        this.reQcError = err.error?.mesaj || 'Kalite kontrolü yapılırken bir hata oluştu. Lütfen tekrar deneyin.';
      }
    });
  }

  isLatestVersion(ver: any): boolean {
    return this.versions.length > 1 && ver?.versiyonNo === this.versions[this.versions.length - 1]?.versiyonNo;
  }

  getVersionTitle(ver: any): string {
    return `Versiyon ${ver?.versiyonNo || ''}` + (ver?.revizeTalimati ? ` - Talimat: ${ver.revizeTalimati}` : '');
  }

  openRevisionModal() {
    this.isRevisionModalOpen = true;
    this.revisionInstruction = '';
    this.revisionError = '';
  }

  closeRevisionModal() {
    if (!this.isRevising) {
      this.isRevisionModalOpen = false;
    }
  }

  appendQuickTag(tagText: string) {
    if (this.revisionInstruction) {
      this.revisionInstruction += ' ' + tagText;
    } else {
      this.revisionInstruction = tagText;
    }
  }

  submitRevision() {
    if (!this.revisionInstruction.trim() || !this.result?.id) return;

    this.isRevising = true;
    this.revisionError = '';

    const payload = {
      revizeTalimati: this.revisionInstruction.trim(),
      hedefAlan: this.revisionTarget
    };

    this.apiService.reviseContent(this.result.id, payload).subscribe({
      next: (updatedResult) => {
        this.result = updatedResult;
        this.initializeVersions();
        this.isRevising = false;
        this.isRevisionModalOpen = false;
        this.revisionInstruction = '';
      },
      error: (err) => {
        console.error('Revizyon hatası:', err);
        this.isRevising = false;
        this.revisionError = err.error?.mesaj || 'Revizyon yapılırken sunucuda bir hata oluştu. Lütfen tekrar deneyin.';
      }
    });
  }

  get activeTabTitle(): string {
    if (this.activeTab === 'ozet') return 'Araştırma Özeti';
    if (this.activeTab === 'plan') return 'Video Planı';
    return 'QC Kalite Raporu';
  }

  /**
   * Kullanıcının girdiği uzun prompt/içerik planından temiz ve kısa bir doküman başlığı üretir.
   */
  get documentTitle(): string {
    const raw = (this.result?.konu || '').trim();
    if (!raw) return 'Eğitim İçerik ve Araştırma Raporu';

    // 1. Tırnak içindeki ana başlığı yakala: örn. "Düşünmenin Algoritması: Mantık İlmi"
    const quoteMatch = raw.match(/^["“']([^"“']+)["”']/);
    if (quoteMatch && quoteMatch[1].trim().length >= 3) {
      return quoteMatch[1].trim();
    }

    // 2. İlk satırı al
    const firstLine = raw.split(/\r?\n/)[0].trim();

    // 3. İlk satırdaki tire veya iki noktadan öncesini kontrol et
    const splitDash = firstLine.split(/\s*[-–—]\s*/);
    if (splitDash[0] && splitDash[0].length >= 5 && splitDash[0].length <= 70) {
      return splitDash[0].replace(/^["“']|["”']$/g, '').trim();
    }

    // 4. İlk satır makul uzunluktaysa (< 75 karakter)
    if (firstLine.length <= 75) {
      return firstLine.replace(/^["“']|["”']$/g, '').trim();
    }

    // 5. Çok uzunsa ilk 65 karakterde kelime sınırından kes
    const truncated = firstLine.slice(0, 65);
    const lastSpace = truncated.lastIndexOf(' ');
    return (lastSpace > 20 ? truncated.slice(0, lastSpace) : truncated).trim() + '...';
  }

  /**
   * Eğer kullanıcı başlık haricinde detaylı not/bölüm planı girdiyse bunu özet bilgi kutusu için döner.
   */
  get promptNotes(): string {
    const raw = (this.result?.konu || '').trim();
    if (!raw) return '';
    if (raw.length <= this.documentTitle.length + 15) return '';
    
    if (raw.length > 600) {
      return raw.slice(0, 600).trim() + '... (ayrıntılı talep özeti)';
    }
    return raw;
  }

  get cleanFileName(): string {
    const ver = this.activeVersion ? `_V${this.activeVersion.versiyonNo}` : '';
    return (this.documentTitle
      .replace(/["'<>:;?*|/\\]/g, '')
      .trim()
      .replace(/\s+/g, '_')
      .slice(0, 40) || 'Icerik_Raporu') + ver;
  }

  toggleDownloadMenu() {
    this.isDownloadMenuOpen = !this.isDownloadMenuOpen;
  }

  downloadDoc(scope: 'active' | 'all') {
    const html = this.generateDocumentHtml(scope, 'word');
    const blob = new Blob(['\ufeff' + html], { type: 'application/msword;charset=utf-8' });
    const suffix = scope === 'all' ? 'Tam_Rapor' : (this.activeTab === 'ozet' ? 'Arastirma_Ozeti' : (this.activeTab === 'plan' ? 'Video_Plani' : 'QC_Raporu'));
    this.saveBlob(blob, `${this.cleanFileName}_${suffix}.doc`);
    this.isDownloadMenuOpen = false;
  }

  downloadPdf(scope: 'active' | 'all') {
    const html = this.generateDocumentHtml(scope, 'print');
    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      alert('Açılır pencere engellendi. Lütfen tarayıcınızın ayarlarından bu site için açılır pencerelere izin verin.');
      return;
    }
    printWindow.document.open();
    printWindow.document.write(html);
    printWindow.document.close();
    printWindow.focus();
    setTimeout(() => {
      printWindow.print();
    }, 400);
    this.isDownloadMenuOpen = false;
  }

  downloadMarkdown(scope: 'active' | 'all') {
    const ozet = this.activeVersion?.arastirmaOzeti ?? (this.result.generatedContent?.arastirmaOzeti ?? '');
    const plan = this.activeVersion?.videoPlani ?? (this.result.generatedContent?.videoPlani ?? '');
    const verStr = this.activeVersion ? ` (Versiyon ${this.activeVersion.versiyonNo})` : '';
    let content = '';

    if (scope === 'all') {
      content = `# ${this.documentTitle}${verStr}\n\n## 1. Araştırma Özeti\n\n${ozet}\n\n---\n\n## 2. Video Planı\n\n${plan}\n\n---\n\n## 3. Kalite Kontrol Raporu (QC)\nGüven Skoru: %${this.result.qcResult?.guvenSkorYuzde ?? 100}\n\n${JSON.stringify(this.parsedQcReport, null, 2)}`;
    } else {
      content = this.activeTab === 'ozet' ? ozet : (this.activeTab === 'plan' ? plan : JSON.stringify(this.parsedQcReport, null, 2));
    }

    const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' });
    const suffix = scope === 'all' ? 'Tam_Rapor' : (this.activeTab === 'ozet' ? 'Arastirma_Ozeti' : 'Video_Plani');
    this.saveBlob(blob, `${this.cleanFileName}_${suffix}.md`);
    this.isDownloadMenuOpen = false;
  }

  copyToClipboard() {
    const ozet = this.activeVersion?.arastirmaOzeti ?? (this.result.generatedContent?.arastirmaOzeti ?? '');
    const plan = this.activeVersion?.videoPlani ?? (this.result.generatedContent?.videoPlani ?? '');
    const text = this.activeTab === 'ozet' ? ozet : (this.activeTab === 'plan' ? plan : JSON.stringify(this.parsedQcReport, null, 2));

    navigator.clipboard.writeText(text).then(() => {
      this.copySuccess = true;
      setTimeout(() => (this.copySuccess = false), 2500);
    });
    this.isDownloadMenuOpen = false;
  }

  private escapeHtml(text: string): string {
    return text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  private saveBlob(blob: Blob, filename: string) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  private generateDocumentHtml(scope: 'active' | 'all', mode: 'word' | 'print'): string {
    const title = this.documentTitle;
    const verLabel = this.activeVersion ? ` - Versiyon ${this.activeVersion.versiyonNo}` : '';
    const dateSource = this.activeVersion?.olusturmaTarihi || this.result?.olusturmaTarihi;
    const dateStr = dateSource
      ? new Date(dateSource).toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
      : new Date().toLocaleDateString('tr-TR');
    const model = this.activeVersion?.llmModel || this.result?.generatedContent?.llmModel || 'Claude-3.5-Sonnet';
    const score = this.result?.qcResult?.guvenSkorYuzde ?? this.result?.guvenSkorYuzde ?? 100;
    const targetLength = this.result?.hedefUzunluk || 'Orta Uzunluk';
    const targetAudience = this.result?.hedefKitle || 'Genel İzleyici';

    let bodyContent = '';

    if (scope === 'all') {
      bodyContent = `
        <div class="section-title">1. Araştırma Özeti</div>
        ${this.parsedOzet || '<p>Araştırma özeti bulunamadı.</p>'}
        
        <div style="page-break-before: always; mso-break-type: section-break; margin-top: 30px;"></div>
        
        <div class="section-title">2. Video İçerik ve Bölüm Planı</div>
        ${this.parsedPlan || '<p>Video planı bulunamadı.</p>'}
        
        <div style="page-break-before: always; mso-break-type: section-break; margin-top: 30px;"></div>
        
        <div class="section-title">3. Kalite Kontrol (QC) Doğrulama Raporu</div>
        ${this.generateQcHtmlTable()}
      `;
    } else {
      if (this.activeTab === 'ozet') {
        bodyContent = `
          <div class="section-title">Araştırma Özeti</div>
          ${this.parsedOzet || '<p>Araştırma özeti bulunamadı.</p>'}
        `;
      } else if (this.activeTab === 'plan') {
        bodyContent = `
          <div class="section-title">Video İçerik ve Bölüm Planı</div>
          ${this.parsedPlan || '<p>Video planı bulunamadı.</p>'}
        `;
      } else {
        bodyContent = `
          <div class="section-title">Kalite Kontrol (QC) Doğrulama Raporu</div>
          ${this.generateQcHtmlTable()}
        `;
      }
    }

    const isWord = mode === 'word';
    const xmlDeclaration = isWord
      ? `xmlns:o='urn:schemas-microsoft-com:office:office' xmlns:w='urn:schemas-microsoft-com:office:word' xmlns='http://www.w3.org/TR/REC-html40'`
      : '';

    const wordMeta = isWord
      ? `<!--[if gte mso 9]>
        <xml>
          <w:WordDocument>
            <w:View>Print</w:View>
            <w:Zoom>100</w:Zoom>
            <w:DoNotOptimizeForBrowser/>
          </w:WordDocument>
        </xml>
        <![endif]-->`
      : '';

    return `
      <!DOCTYPE html>
      <html ${xmlDeclaration}>
      <head>
        <meta charset="utf-8">
        <title>${title}${verLabel}</title>
        ${wordMeta}
        <style>
          @page WordSection1 {
            size: 595.3pt 841.9pt;
            margin: 54.0pt 54.0pt 54.0pt 54.0pt;
            mso-header-margin: 36.0pt;
            mso-footer-margin: 36.0pt;
          }
          @page {
            size: A4;
            margin: 15mm 18mm;
          }
          div.WordSection1 {
            page: WordSection1;
          }
          body {
            font-family: 'Calibri', 'Segoe UI', Arial, sans-serif;
            font-size: 10.5pt;
            line-height: 1.55;
            color: #1e293b;
            background: #ffffff;
            margin: 0;
            padding: ${mode === 'print' ? '10px' : '0'};
          }
          .header-box {
            border-bottom: 2px solid #1e3a8a;
            padding-bottom: 8px;
            margin-bottom: 16px;
          }
          .doc-main-title {
            font-size: 18pt;
            font-weight: bold;
            color: #1e3a8a;
            margin: 0 0 4px 0;
            line-height: 1.25;
          }
          .doc-subtitle {
            font-size: 10pt;
            color: #64748b;
            margin: 0;
          }
          .meta-table {
            width: 100%;
            border-collapse: collapse;
            background-color: #f8fafc;
            border: 1px solid #e2e8f0;
            margin-bottom: 16px;
          }
          .meta-table td {
            padding: 5px 10px;
            font-size: 9pt;
            border: 1px solid #e2e8f0;
          }
          .meta-label {
            font-weight: bold;
            color: #475569;
            width: 130px;
            background-color: #f1f5f9;
          }
          .prompt-box {
            background-color: #f8fafc;
            border: 1px solid #e2e8f0;
            border-left: 3.5px solid #3b82f6;
            padding: 8px 12px;
            border-radius: 4px;
            margin-bottom: 20px;
          }
          .prompt-label {
            font-size: 8.5pt;
            font-weight: bold;
            color: #475569;
            text-transform: uppercase;
            letter-spacing: 0.02em;
            margin-bottom: 4px;
          }
          .prompt-text {
            font-size: 9pt;
            color: #334155;
            line-height: 1.5;
            font-style: italic;
            white-space: pre-wrap;
          }
          .section-title {
            font-size: 15pt;
            font-weight: bold;
            color: #1e3a8a;
            border-bottom: 2px solid #2563eb;
            padding-bottom: 5px;
            margin-top: 20px;
            margin-bottom: 12px;
          }
          h1 {
            font-size: 14pt;
            color: #1e3a8a;
            font-weight: bold;
            margin-top: 18px;
            margin-bottom: 8px;
          }
          h2 {
            font-size: 12.5pt;
            color: #1d4ed8;
            font-weight: bold;
            border-bottom: 1px solid #bfdbfe;
            padding-bottom: 3px;
            margin-top: 16px;
            margin-bottom: 6px;
          }
          h3 {
            font-size: 11.5pt;
            color: #2563eb;
            font-weight: bold;
            margin-top: 12px;
            margin-bottom: 5px;
          }
          h4 {
            font-size: 10.5pt;
            color: #0f172a;
            font-weight: bold;
            margin-top: 10px;
            margin-bottom: 4px;
          }
          p {
            margin-top: 0;
            margin-bottom: 8px;
          }
          ul, ol {
            margin-top: 0;
            margin-bottom: 10px;
            padding-left: 22px;
          }
          li {
            margin-bottom: 3px;
          }
          strong {
            color: #0f172a;
            font-weight: bold;
          }
          blockquote {
            border-left: 3.5px solid #6366f1;
            padding: 6px 12px;
            background-color: #f8fafc;
            color: #334155;
            font-style: italic;
            margin: 12px 0;
          }
          table {
            border-collapse: collapse;
            width: 100%;
            margin: 14px 0;
          }
          th, td {
            border: 1px solid #cbd5e1;
            padding: 6px 8px;
            text-align: left;
            font-size: 9.5pt;
          }
          th {
            background-color: #f1f5f9;
            color: #1e293b;
            font-weight: bold;
          }
          @media print {
            body { padding: 0; }
            .header-box { margin-bottom: 12px; }
          }
        </style>
      </head>
      <body>
        <div class="WordSection1">
          <div class="header-box">
            <h1 class="doc-main-title">${title}${verLabel}</h1>
            <p class="doc-subtitle">VideoOzet Yapay Zeka Destekli İçerik ve Araştırma Dokümanı</p>
          </div>

          <table class="meta-table">
            <tr>
              <td class="meta-label">Oluşturma Tarihi:</td>
              <td>${dateStr}</td>
              <td class="meta-label">LLM Modeli:</td>
              <td>${model}</td>
            </tr>
            <tr>
              <td class="meta-label">Hedef Uzunluk:</td>
              <td>${targetLength}</td>
              <td class="meta-label">Hedef Kitle:</td>
              <td>${targetAudience}</td>
            </tr>
            <tr>
              <td class="meta-label">Kalite Güven Skoru:</td>
              <td colspan="3" style="font-weight: bold; color: ${score >= 80 ? '#16a34a' : '#d97706'};">%${score}</td>
            </tr>
            ${this.activeVersion?.revizeTalimati ? `
            <tr>
              <td class="meta-label">Versiyon Notu:</td>
              <td colspan="3" style="font-style: italic; color: #334155;">${this.escapeHtml(this.activeVersion.revizeTalimati)}</td>
            </tr>
            ` : ''}
          </table>

          ${this.promptNotes ? `
            <div class="prompt-box">
              <div class="prompt-label">📌 Talep Edilen Konu & Çerçeve Özeti:</div>
              <div class="prompt-text">${this.escapeHtml(this.promptNotes)}</div>
            </div>
          ` : ''}

          ${bodyContent}
        </div>
      </body>
      </html>
    `;
  }

  private generateQcHtmlTable(): string {
    if (!this.parsedQcReport || this.parsedQcReport.length === 0) {
      return '<p>Kalite kontrol raporu bulunamadı.</p>';
    }

    const rows = this.parsedQcReport.map(item => {
      let icon = '✅ Desteklendi';
      let color = '#16a34a';
      if (item.durum === 'belirsiz') {
        icon = '⚠️ Belirsiz';
        color = '#d97706';
      } else if (item.durum === 'desteklenmedi') {
        icon = '❌ Desteklenmedi';
        color = '#dc2626';
      }

      return `
        <tr>
          <td style="width: 140px; font-weight: bold; color: ${color}; vertical-align: top;">${icon}</td>
          <td>
            <strong>${item.iddia}</strong>
            <div style="color: #475569; font-size: 9pt; margin-top: 3px;"><em>Kaynak Açıklaması:</em> ${item.aciklama || '-'}</div>
          </td>
        </tr>
      `;
    }).join('');

    return `
      <table>
        <thead>
          <tr>
            <th style="width: 140px;">Doğrulama Durumu</th>
            <th>İddia ve Kaynak Doğrulama Notu</th>
          </tr>
        </thead>
        <tbody>
          ${rows}
        </tbody>
      </table>
    `;
  }
}
