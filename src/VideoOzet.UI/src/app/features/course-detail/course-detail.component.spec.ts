import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CourseDetailComponent } from './course-detail.component';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ActivatedRoute, Router } from '@angular/router';
import { of, Subject } from 'rxjs';

describe('CourseDetailComponent', () => {
  let component: CourseDetailComponent;
  let fixture: ComponentFixture<CourseDetailComponent>;
  let mockApiService: any;
  let mockSignalRService: any;
  let mockRouter: any;

  beforeEach(async () => {
    mockApiService = {
      getEgitim: jasmine.createSpy('getEgitim').and.returnValue(of({ id: '123', ad: 'Test Egitim', videos: [] })),
      getEgitimVideos: jasmine.createSpy('getEgitimVideos').and.returnValue(of([{ id: 'v1', asama: 'Bekliyor' }])),
      createContentRequest: jasmine.createSpy('createContentRequest').and.returnValue(of({ success: true })),
      getContentRequest: jasmine.createSpy('getContentRequest').and.returnValue(of({ id: 'cr1', result: 'test' }))
    };

    mockSignalRService = {
      startConnection: jasmine.createSpy('startConnection'),
      pipelineStageChanged$: new Subject<any>(),
      contentGenerated$: new Subject<any>()
    };

    mockRouter = {
      navigate: jasmine.createSpy('navigate')
    };

    const mockActivatedRoute = {
      snapshot: {
        paramMap: {
          get: () => '123'
        }
      }
    };

    await TestBed.configureTestingModule({
      imports: [CourseDetailComponent],
      providers: [
        { provide: ApiService, useValue: mockApiService },
        { provide: SignalRService, useValue: mockSignalRService },
        { provide: Router, useValue: mockRouter },
        { provide: ActivatedRoute, useValue: mockActivatedRoute }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(CourseDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load egitim and videos via forkJoin on init', () => {
    expect(component).toBeTruthy();
    expect(mockApiService.getEgitim).toHaveBeenCalledWith('123');
    expect(mockApiService.getEgitimVideos).toHaveBeenCalledWith('123');
    expect(component.egitim.ad).toBe('Test Egitim');
    expect(component.egitim.videos.length).toBe(1);
    expect(component.isLoading).toBeFalse();
  });

  it('should update video status when pipelineStageChanged$ emits', () => {
    // Initial state
    expect(component.egitim.videos[0].sonAsama).toBeUndefined();

    // Emit event
    mockSignalRService.pipelineStageChanged$.next({
      videoId: 'v1',
      asama: 'Özetleme',
      durum: 'İşleniyor',
      mesaj: null
    });

    expect(component.egitim.videos[0].sonAsama).toBe('Özetleme');
    expect(component.egitim.videos[0].durum).toBe('İşleniyor');
  });

  it('should load content request data when contentGenerated$ emits', () => {
    component.isRequestingContent = true;

    mockSignalRService.contentGenerated$.next({
      contentRequestId: 'cr1'
    });

    expect(mockApiService.getContentRequest).toHaveBeenCalledWith('cr1');
    expect(component.isRequestingContent).toBeFalse();
    expect(component.contentResult).toEqual({ id: 'cr1', result: 'test' });
  });

  it('should start requesting content when requestContent is called', () => {
    component.contentTopic = 'Deneme Konu';
    component.requestContent();

    expect(component.isRequestingContent).toBeTrue();
    expect(mockApiService.createContentRequest).toHaveBeenCalled();
  });
});
