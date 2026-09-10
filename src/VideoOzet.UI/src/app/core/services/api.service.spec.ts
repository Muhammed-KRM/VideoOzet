import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ApiService } from './api.service';
import { environment } from '../../../environments/environment';

describe('ApiService', () => {
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

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should fetch egitim details via getEgitim', () => {
    const mockData = { id: '123', ad: 'Test Egitim' };
    
    service.getEgitim('123').subscribe(data => {
      expect(data).toEqual(mockData);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/egitimler/123`);
    expect(req.request.method).toBe('GET');
    req.flush(mockData);
  });

  it('should fetch egitim videos via getEgitimVideos', () => {
    const mockData = [{ id: 'v1', baslik: 'Test Video' }];
    
    service.getEgitimVideos('123').subscribe(data => {
      expect(data).toEqual(mockData);
      expect(data.length).toBe(1);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/egitimler/123/videolar`);
    expect(req.request.method).toBe('GET');
    req.flush(mockData);
  });

  it('should post video upload to correct endpoint', () => {
    const formData = new FormData();
    formData.append('file', new Blob(['test'], { type: 'video/mp4' }), 'test.mp4');
    const mockResponse = { success: true };

    service.uploadVideo('123', formData).subscribe(res => {
      expect(res).toEqual(mockResponse);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/egitimler/123/videolar/upload`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBe(formData);
    req.flush(mockResponse);
  });

  it('should handle API errors properly', () => {
    service.getEgitim('123').subscribe({
      next: () => fail('should have failed with the 500 error'),
      error: (error) => {
        expect(error.status).toBe(500);
      }
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/egitimler/123`);
    req.flush('Failed!', { status: 500, statusText: 'Server Error' });
  });
});
