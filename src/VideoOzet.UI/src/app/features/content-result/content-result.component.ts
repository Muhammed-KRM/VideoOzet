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

  ngOnChanges() {
    if (this.result) {
      this.parsedOzet = marked.parse(this.result.arastirmaOzeti || '') as string;
      this.parsedPlan = marked.parse(this.result.videoPlani || '') as string;
      this.activeTab = 'ozet';
    }
  }
}
