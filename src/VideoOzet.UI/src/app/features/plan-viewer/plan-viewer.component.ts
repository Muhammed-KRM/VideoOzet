import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-plan-viewer',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './plan-viewer.component.html'
})
export class PlanViewerComponent implements OnChanges {
  @Input() revision: any;

  activeTab: 'ozet' | 'plan' | 'qc' = 'plan';
  qcRaporList: any[] = [];
  formattedOzet: string = '';
  formattedPlan: string = '';

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
        this.qcRaporList = JSON.parse(this.revision.detayliRapor);
      } catch {
        // Hata durumunda liste bos kalir
      }
    }
  }

  downloadMarkdown() {
    if (!this.revision) return;
    
    let content = `# Araştırma Özeti\n\n${this.revision.arastirmaOzeti || 'Boş'}\n\n`;
    content += `---\n\n`;
    content += `# Video Planı\n\n${this.revision.videoPlani || 'Boş'}`;

    const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `Bolum_${this.revision.bolumNo || 'Plan'}_V${this.revision.versiyonNo || 1}.md`;
    a.click();
    window.URL.revokeObjectURL(url);
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
