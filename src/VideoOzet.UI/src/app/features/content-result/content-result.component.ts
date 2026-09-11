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
      const ozet = this.result.generatedContent?.arastirmaOzeti ?? this.result.arastirmaOzeti ?? '';
      const plan = this.result.generatedContent?.videoPlani ?? this.result.videoPlani ?? '';
      
      this.parsedOzet = ozet ? (marked.parse(ozet) as string) : '';
      this.parsedPlan = plan ? (marked.parse(plan) as string) : '';
      
      const rawQc = this.result.qcResult?.detayliRapor ?? this.result.detayliRapor;
      try {
        this.parsedQcReport = typeof rawQc === 'string'
          ? JSON.parse(rawQc)
          : (rawQc || []);
      } catch (e) {
        this.parsedQcReport = [];
      }
      
      this.activeTab = 'ozet';
    }
  }
}
