import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { marked } from 'marked';

@Component({
  selector: 'app-content-result',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './content-result.component.html'
})
export class ContentResultComponent implements OnChanges {
  @Input() result: any = null;
  @Input() isLoading = false;

  activeTab = 'ozet'; // 'ozet', 'plan', 'qc'
  
  parsedOzet = '';
  parsedPlan = '';
  parsedQcReport: any[] = [];


  ngOnChanges() {
    if (this.result) {
      this.parsedOzet = marked.parse(this.result.arastirmaOzeti || '') as string;
      this.parsedPlan = marked.parse(this.result.videoPlani || '') as string;
      
      try {
        this.parsedQcReport = typeof this.result.qcResult?.detayliRapor === 'string'
          ? JSON.parse(this.result.qcResult.detayliRapor)
          : this.result.qcResult?.detayliRapor || [];
      } catch (e) {
        this.parsedQcReport = [];
      }
      
      this.activeTab = 'ozet';
    }
  }
}
