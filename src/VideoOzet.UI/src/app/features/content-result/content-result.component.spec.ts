import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContentResultComponent } from './content-result.component';

describe('ContentResultComponent', () => {
  let component: ContentResultComponent;
  let fixture: ComponentFixture<ContentResultComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContentResultComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ContentResultComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should parse valid QC JSON string correctly', () => {
    component.result = {
      qcResult: {
        detayliRapor: '[{"iddia": "Test iddia", "durum": "desteklendi"}]'
      }
    };
    
    component.ngOnChanges();
    
    expect(component.parsedQcReport).toBeTruthy();
    expect(component.parsedQcReport.length).toBe(1);
    expect(component.parsedQcReport[0].iddia).toBe('Test iddia');
  });

  it('should handle invalid QC JSON string without throwing error', () => {
    component.result = {
      qcResult: {
        detayliRapor: 'invalid json string'
      }
    };
    
    // Should not throw error
    expect(() => component.ngOnChanges()).not.toThrow();
    
    expect(component.parsedQcReport).toEqual([]);
  });

  it('should handle pre-parsed QC array correctly', () => {
    component.result = {
      qcResult: {
        detayliRapor: [{ iddia: "Test", durum: "desteklendi" }]
      }
    };
    
    component.ngOnChanges();
    
    expect(component.parsedQcReport.length).toBe(1);
    expect(component.parsedQcReport[0].iddia).toBe('Test');
  });

  it('should parse markdown to html', () => {
    component.result = {
      arastirmaOzeti: '# Başlık',
      videoPlani: '**Kalın Yazı**'
    };
    
    component.ngOnChanges();
    
    expect(component.parsedOzet).toContain('<h1>Başlık</h1>');
    expect(component.parsedPlan).toContain('<strong>Kalın Yazı</strong>');
  });
});
