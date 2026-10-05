import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EpisodeViewerComponent } from './episode-viewer.component';

describe('EpisodeViewerComponent', () => {
  let component: EpisodeViewerComponent;
  let fixture: ComponentFixture<EpisodeViewerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EpisodeViewerComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(EpisodeViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
