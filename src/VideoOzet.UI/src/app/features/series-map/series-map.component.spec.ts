import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SeriesMapComponent } from './series-map.component';

describe('SeriesMapComponent', () => {
  let component: SeriesMapComponent;
  let fixture: ComponentFixture<SeriesMapComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SeriesMapComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(SeriesMapComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
