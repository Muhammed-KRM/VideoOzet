import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { YoutubeBrowserComponent } from './youtube-browser.component';
import { ApiService } from '../../core/services/api.service';

describe('YoutubeBrowserComponent', () => {
  let component: YoutubeBrowserComponent;
  let fixture: ComponentFixture<YoutubeBrowserComponent>;
  let mockApiService: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    mockApiService = jasmine.createSpyObj<ApiService>('ApiService', [
      'searchYouTube',
      'getYouTubeVideoDetail',
      'importRemoteVideos'
    ]);

    await TestBed.configureTestingModule({
      imports: [YoutubeBrowserComponent],
      providers: [{ provide: ApiService, useValue: mockApiService }]
    }).compileComponents();

    fixture = TestBed.createComponent(YoutubeBrowserComponent);
    component = fixture.componentInstance;
    component.egitimId = 'egitim-1';
    fixture.detectChanges();
  });

  it('should create without making HTTP requests on init', () => {
    expect(component).toBeTruthy();
    expect(mockApiService.searchYouTube).not.toHaveBeenCalled();
  });

  it('should search YouTube and populate results with duration filter', () => {
    mockApiService.searchYouTube.and.returnValue(
      of({
        items: [
          {
            videoId: 'dQw4w9WgXcQ',
            baslik: 'RAG Mimarisi',
            kanalAdi: 'Tech Kanal',
            thumbnailUrl: 'https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg',
            sureSaniye: 620,
            sureMetni: '10:20',
            kaynakUrl: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'
          }
        ],
        kullanilanKaynak: 'yt-dlp',
        kotaAsildiMi: false,
        uyariMesaji: null
      })
    );

    component.searchQuery = 'RAG Mimarisi';
    component.durationFilter = 'medium';
    component.search();

    expect(mockApiService.searchYouTube).toHaveBeenCalledWith('RAG Mimarisi', 15, 'medium');
    expect(component.results.length).toBe(1);
    expect(component.kullanilanKaynak).toBe('yt-dlp');
  });

  it('should toggle multi-select without opening preview modal', () => {
    const item = {
      videoId: 'dQw4w9WgXcQ',
      baslik: 'Test',
      kanalAdi: 'Kanal',
      thumbnailUrl: 'https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg',
      sureMetni: '05:00',
      kaynakUrl: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'
    };
    const event = new MouseEvent('click');
    spyOn(event, 'stopPropagation');

    component.toggleSelect(item, event);

    expect(event.stopPropagation).toHaveBeenCalled();
    expect(component.isSelected('dQw4w9WgXcQ')).toBeTrue();
    expect(component.previewItem).toBeNull();
  });

  it('should reject invalid YouTube videoId before calling DomSanitizer', () => {
    const invalidItem = {
      videoId: 'bad<script>',
      baslik: 'Invalid',
      kanalAdi: 'Kanal',
      thumbnailUrl: '',
      sureMetni: '',
      kaynakUrl: ''
    };

    component.openPreview(invalidItem);

    expect(component.previewEmbedUrl).toBeNull();
    expect(component.detailError).toContain('Geçersiz');
    expect(mockApiService.getYouTubeVideoDetail).not.toHaveBeenCalled();
  });

  it('should build youtube-nocookie embed url and fetch detail for valid 11-char ID', () => {
    const validItem = {
      videoId: 'dQw4w9WgXcQ',
      baslik: 'Valid Video',
      kanalAdi: 'Kanal',
      thumbnailUrl: 'https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg',
      sureMetni: '10:00',
      kaynakUrl: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'
    };
    mockApiService.getYouTubeVideoDetail.and.returnValue(
      of({
        ...validItem,
        aciklama: 'Açıklama',
        manuelAltyaziDilleri: ['tr'],
        otomatikAltyaziDilleri: ['en'],
        ilgiliVideolar: []
      })
    );

    component.openPreview(validItem);

    expect(component.previewEmbedUrl).not.toBeNull();
    expect(mockApiService.getYouTubeVideoDetail).toHaveBeenCalledWith('dQw4w9WgXcQ');
    expect(component.previewDetail?.manuelAltyaziDilleri).toEqual(['tr']);
  });

  it('should import selected videos and handle 409 duplicate conflict', () => {
    const item = {
      videoId: 'dQw4w9WgXcQ',
      baslik: 'Test',
      kanalAdi: 'Kanal',
      thumbnailUrl: '',
      sureSaniye: 300,
      sureMetni: '05:00',
      kaynakUrl: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'
    };
    component.toggleSelect(item);
    component.selectedAlimModu = 0;

    mockApiService.importRemoteVideos.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              eklenenVideolar: [],
              atlananDuplicateIdler: ['dQw4w9WgXcQ'],
              hatalar: [],
              mesaj: '0 video eklendi, 1 mükerrer video atlandı.'
            }
          })
      )
    );

    component.importSelected();

    expect(component.importWarning).toContain('mükerrer');
  });

  it('should extract RFC 7807 PascalCase Detail from HTTP 400 search error response', () => {
    mockApiService.searchYouTube.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            statusText: 'Bad Request',
            error: {
              Type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
              Title: 'Business Rule Violation',
              Status: 400,
              Detail: 'yt-dlp ikilisi bulunamadı.'
            }
          })
      )
    );

    component.searchQuery = 'test arama';
    component.search();

    expect(component.searchError).toBe('yt-dlp ikilisi bulunamadı.');
  });

  it('should extract RFC 7807 camelCase detail from HTTP 400 import error response', () => {
    const item = {
      videoId: 'dQw4w9WgXcQ',
      baslik: 'Test',
      kanalAdi: 'Kanal',
      thumbnailUrl: '',
      sureSaniye: 300,
      sureMetni: '05:00',
      kaynakUrl: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'
    };
    component.toggleSelect(item);

    mockApiService.importRemoteVideos.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            statusText: 'Bad Request',
            error: {
              type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
              title: 'Business Rule Violation',
              status: 400,
              detail: 'Geçersiz uzaktan video isteği.'
            }
          })
      )
    );

    component.importSelected();

    expect(component.importError).toBe('Geçersiz uzaktan video isteği.');
  });
});
