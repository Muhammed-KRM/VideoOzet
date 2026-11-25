import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ApiService } from './api.service';
import { environment } from '../../../environments/environment';

describe('ApiService (Remote Import & YouTube)', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ApiService]
    });
    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should search YouTube with query, maxResults and duration params', () => {
    const mockRes = { items: [], kullanilanKaynak: 'yt-dlp', kotaAsildiMi: false, uyariMesaji: null };
    service.searchYouTube('rag', 10, 'long').subscribe((res) => {
      expect(res).toEqual(mockRes);
    });

    const req = httpMock.expectOne(
      `${environment.apiUrl}/youtube/search?q=rag&maxResults=10&duration=long`
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockRes);
  });

  it('should fetch YouTube video detail via getYouTubeVideoDetail', () => {
    const mockDetail: any = { videoId: 'dQw4w9WgXcQ', baslik: 'Test', manuelAltyaziDilleri: ['tr'] };
    service.getYouTubeVideoDetail('dQw4w9WgXcQ').subscribe((res) => {
      expect(res.videoId).toBe('dQw4w9WgXcQ');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/youtube/video/dQw4w9WgXcQ`);
    expect(req.request.method).toBe('GET');
    req.flush(mockDetail);
  });

  it('should inspect remote url and import remote videos', () => {
    service.inspectRemoteUrl('e1', { url: 'https://youtu.be/dQw4w9WgXcQ' }).subscribe();
    const inspectReq = httpMock.expectOne(`${environment.apiUrl}/egitimler/e1/videolar/inspect-url`);
    expect(inspectReq.request.method).toBe('POST');
    inspectReq.flush({ items: [], errors: [] });

    service
      .importRemoteVideos('e1', {
        items: [{ kaynakUrl: 'https://youtu.be/dQw4w9WgXcQ', kaynakPlatform: 1 }],
        alimModu: 0,
        kullanimTuru: 0,
        indekslemeTuru: 0
      })
      .subscribe();
    const importReq = httpMock.expectOne(`${environment.apiUrl}/egitimler/e1/videolar/import-remote`);
    expect(importReq.request.method).toBe('POST');
    importReq.flush({ eklenenVideolar: [], atlananDuplicateIdler: [], hatalar: [], mesaj: 'ok' });
  });
});
