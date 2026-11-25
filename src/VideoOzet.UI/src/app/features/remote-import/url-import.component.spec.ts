import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { UrlImportComponent } from './url-import.component';
import { ApiService } from '../../core/services/api.service';

describe('UrlImportComponent', () => {
  let component: UrlImportComponent;
  let fixture: ComponentFixture<UrlImportComponent>;
  let mockApiService: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    mockApiService = jasmine.createSpyObj<ApiService>('ApiService', [
      'inspectRemoteUrl',
      'importRemoteVideos'
    ]);

    await TestBed.configureTestingModule({
      imports: [UrlImportComponent],
      providers: [{ provide: ApiService, useValue: mockApiService }]
    }).compileComponents();

    fixture = TestBed.createComponent(UrlImportComponent);
    component = fixture.componentInstance;
    component.egitimId = 'egitim-1';
    component.mode = 'direct-url';
    fixture.detectChanges();
  });

  it('should create without making HTTP requests on init', () => {
    expect(component).toBeTruthy();
    expect(mockApiService.inspectRemoteUrl).not.toHaveBeenCalled();
  });

  it('should inspect URLs with bearerToken and block DRM items from selection', () => {
    mockApiService.inspectRemoteUrl.and.returnValue(
      of({
        items: [
          {
            disKaynakId: 'open-1',
            baslik: 'Bölüm 1',
            kaynakUrl: 'https://cdn.example.com/1.mp4',
            kaynakPlatform: 'DirectUrl',
            altyaziMevcutMu: false,
            mevcutAltyaziDilleri: [],
            zatenEgitimdeVarMi: false,
            drmKorumaliMi: false
          },
          {
            disKaynakId: 'drm-1',
            baslik: 'Korumalı Video',
            kaynakUrl: 'https://cdn.example.com/drm.m3u8',
            kaynakPlatform: 'DirectUrl',
            altyaziMevcutMu: false,
            mevcutAltyaziDilleri: [],
            zatenEgitimdeVarMi: false,
            drmKorumaliMi: true,
            hataMesaji: 'DRM korumalı içerik desteklenmez.'
          }
        ],
        errors: []
      })
    );

    component.urlInput = 'https://cdn.example.com/manifest.json';
    component.bearerToken = 'secret-token';
    component.inspect();

    expect(mockApiService.inspectRemoteUrl).toHaveBeenCalledWith('egitim-1', {
      url: 'https://cdn.example.com/manifest.json',
      bearerToken: 'secret-token'
    });
    expect(component.selectedCount).toBe(1);
    expect(component.isItemBlocked(component.previewItems[1])).toBeTrue();

    component.toggleItem(component.previewItems[1]);
    expect(component.isItemSelected(component.previewItems[1])).toBeFalse();
  });

  it('should import selected items and emit importComplete', () => {
    spyOn(component.importComplete, 'emit');
    component.previewItems = [
      {
        disKaynakId: 'open-1',
        baslik: 'Bölüm 1',
        kaynakUrl: 'https://cdn.example.com/1.mp4',
        kaynakPlatform: 'DirectUrl',
        altyaziMevcutMu: false,
        mevcutAltyaziDilleri: [],
        zatenEgitimdeVarMi: false
      }
    ];
    component.toggleItem(component.previewItems[0]);
    component.bearerToken = 'tok-123';
    component.selectedAlimModu = 1;

    mockApiService.importRemoteVideos.and.returnValue(
      of({
        eklenenVideolar: [
          {
            id: 'v1',
            egitimId: 'egitim-1',
            baslik: 'Bölüm 1',
            dosyaYolu: '',
            islemDurumu: 'Indiriliyor',
            kullanimTuru: 0,
            indekslemeTuru: 0,
            olusturmaTarihi: '2026-11-23T13:00:00Z'
          }
        ],
        atlananDuplicateIdler: [],
        hatalar: [],
        mesaj: '1 video eklendi.'
      })
    );

    component.importSelected();

    expect(mockApiService.importRemoteVideos).toHaveBeenCalledWith('egitim-1', {
      items: [
        {
          kaynakUrl: 'https://cdn.example.com/1.mp4',
          baslik: 'Bölüm 1',
          disKaynakId: 'open-1',
          kaynakPlatform: 'DirectUrl',
          kanalAdi: null,
          thumbnailUrl: null,
          sureSaniye: null,
          transcriptUrl: null
        }
      ],
      alimModu: 1,
      kullanimTuru: 0,
      indekslemeTuru: 0,
      tercihEdilenDil: 'tr',
      bearerToken: 'tok-123'
    });
    expect(component.importComplete.emit).toHaveBeenCalled();
    expect(component.importFeedback).toBe('1 video eklendi.');
  });

  it('should extract RFC 7807 PascalCase Detail from HTTP 400 inspect error response', () => {
    mockApiService.inspectRemoteUrl.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            statusText: 'Bad Request',
            error: {
              Type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
              Title: 'Business Rule Violation',
              Status: 400,
              Detail: 'DRM korumalı içerik aktarılamaz.'
            }
          })
      )
    );

    component.urlInput = 'https://cdn.example.com/protected.m3u8';
    component.inspect();

    expect(component.inspectErrors).toEqual(['DRM korumalı içerik aktarılamaz.']);
  });

  it('should extract RFC 7807 camelCase detail from HTTP 400 import error response', () => {
    component.previewItems = [
      {
        disKaynakId: 'open-1',
        baslik: 'Bölüm 1',
        kaynakUrl: 'https://cdn.example.com/1.mp4',
        kaynakPlatform: 'DirectUrl',
        altyaziMevcutMu: false,
        mevcutAltyaziDilleri: [],
        zatenEgitimdeVarMi: false
      }
    ];
    component.toggleItem(component.previewItems[0]);

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
              detail: 'Desteklenmeyen uzak URL biçimi.'
            }
          })
      )
    );

    component.importSelected();

    expect(component.importError).toBe('Desteklenmeyen uzak URL biçimi.');
  });
});
