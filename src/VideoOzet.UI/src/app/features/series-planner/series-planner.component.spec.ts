import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SeriesPlannerComponent } from './series-planner.component';

describe('SeriesPlannerComponent', () => {
  let component: SeriesPlannerComponent;
  let fixture: ComponentFixture<SeriesPlannerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SeriesPlannerComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(SeriesPlannerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
