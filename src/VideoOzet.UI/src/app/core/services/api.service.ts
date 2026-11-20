import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface CourseSourceCardDto {
  kaynakId: string;
  kaynakTuru: string;
  baslik: string;
  dil: string;
  kullanimTuru: string;
  tahminiToplamSureDk?: number;
  konular?: string[];
  ozetBilgi?: string;
}

export interface SeriesSceneOutlineItemDto {
  sahneNo: number;
  baslik: string;
  tahminiSureDk: number;
  aciklamaVeArgumanlar: string;
  atananKaynakKodlari?: string[];
}

export interface UpdateEpisodeItemDto {
  bolumNo: number;
  calismaBasligi: string;
  kanca: string;
  anaFikir: string;
  hedefSureDk: number;
  konular: string[];
  sahneIskeleti: SeriesSceneOutlineItemDto[];
  atananKaynakIdleri: string[];
}

export interface UpdatePlanDto {
  bolumler: UpdateEpisodeItemDto[];
}

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;


  validateApiKey(): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/egitimler`);
  }

  getEgitimler(): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/egitimler`);
  }

  createEgitim(egitim: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler`, egitim);
  }

  getEgitim(id: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/egitimler/${id}`);
  }

  cloneEgitim(id: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${id}/clone`, {});
  }

  uploadFile(egitimId: string, formData: FormData): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/upload`, formData);
  }

  uploadBatchFiles(egitimId: string, formData: FormData): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/upload-batch`, formData);
  }

  deleteVideo(egitimId: string, videoId: string): Observable<any> {
    return this.http.delete<any>(`${this.baseUrl}/egitimler/${egitimId}/videolar/${videoId}`);
  }

  retryVideo(egitimId: string, videoId: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/videolar/${videoId}/retry`, {});
  }

  retryDokuman(egitimId: string, dokumanId: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/dokumanlar/${dokumanId}/retry`, {});
  }

  recoverProblematicFiles(egitimId: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/sorunlulari-kurtar`, {});
  }

  reindexEgitimFiles(egitimId: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/yeniden-indeksle`, {});
  }

  resetQueue(egitimId: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/videolar/reset-queue`, {});
  }

  deleteDokuman(egitimId: string, dokumanId: string): Observable<any> {
    return this.http.delete<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/dokumanlar/${dokumanId}`);
  }

  updateKaynakKullanimTuru(egitimId: string, tip: 'video' | 'dokuman', id: string, kullanimTuru: number): Observable<any> {
    return this.http.patch<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/${tip}/${id}/kullanim-turu`, { kullanimTuru });
  }

  getVideoIcerik(egitimId: string, videoId: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/egitimler/${egitimId}/videolar/${videoId}/icerik`);
  }

  getDokumanIcerik(egitimId: string, dokumanId: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/dokumanlar/${dokumanId}/icerik`);
  }

  updateEgitim(id: string, egitim: any): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/egitimler/${id}`, egitim);
  }

  deleteEgitim(id: string): Observable<any> {
    return this.http.delete<any>(`${this.baseUrl}/egitimler/${id}`);
  }

  getEgitimVideos(egitimId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/egitimler/${egitimId}/videolar`);
  }

  getEgitimDokumanlar(egitimId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/dokumanlar`);
  }

  createContentRequest(egitimId: string, payload: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/content-requests`, payload);
  }

  getContentRequests(egitimId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/egitimler/${egitimId}/content-requests`);
  }

  getContentRequest(id: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/content-requests/${id}`);
  }

  reviseContent(id: string, payload: { revizeTalimati: string, hedefAlan?: string }): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/content-requests/${id}/revise`, payload);
  }

  reRunQualityCheck(id: string, versionNo: number): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/content-requests/${id}/versions/${versionNo}/re-qc`, {});
  }

  // --- v3 Series Planner Endpoints ---

  createSeriesPlan(egitimId: string, payload: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/content-plans`, payload);
  }

  getSeriesPlan(id: string, planNo?: number | null): Observable<any> {
    const suffix = planNo != null && planNo > 0 ? `?planNo=${planNo}` : '';
    return this.http.get<any>(`${this.baseUrl}/content-plans/${id}${suffix}`);
  }

  generatePlanDraft(id: string, draftDto: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/content-plans/${id}/plans/draft`, draftDto);
  }

  updatePlanTexts(id: string, planNo: number, updates: UpdatePlanDto | any): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/content-plans/${id}/plans/${planNo}`, updates);
  }

  approvePlan(id: string, planNo: number, talimat?: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/series-requests/${id}/plans/${planNo}/approve`, { talimat: talimat || '' });
  }

  getEpisodeDetails(id: string, planNo: number, bolumNo: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/series-requests/${id}/plans/${planNo}/videos/${bolumNo}`);
  }

  reviseEpisode(id: string, bolumNo: number, dto: any, planNo?: number | null): Observable<any> {
    const suffix = planNo != null && planNo > 0 ? `?planNo=${planNo}` : '';
    return this.http.post<any>(`${this.baseUrl}/series-requests/${id}/videos/${bolumNo}/revisions${suffix}`, dto);
  }

  retryEpisode(id: string, bolumNo: number, planNo?: number | null): Observable<any> {
    const suffix = planNo != null && planNo > 0 ? `?planNo=${planNo}` : '';
    return this.http.post<any>(`${this.baseUrl}/series-requests/${id}/videos/${bolumNo}/retry${suffix}`, {});
  }

  cancelSeriesProcess(id: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/content-plans/${id}/cancel`, {});
  }

  reRunEpisodeQc(id: string, bolumNo: number, revizyonNo: number): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/series-requests/${id}/videos/${bolumNo}/revisions/${revizyonNo}/re-qc`, {});
  }

  updateEpisodeRevision(id: string, bolumNo: number, revizyonNo: number, payload: { arastirmaOzeti?: string; videoPlani?: string }, planNo?: number | null): Observable<any> {
    const suffix = planNo != null && planNo > 0 ? `?planNo=${planNo}` : '';
    return this.http.put<any>(`${this.baseUrl}/series-requests/${id}/videos/${bolumNo}/revisions/${revizyonNo}${suffix}`, payload);
  }

  deleteEpisodeRevision(id: string, bolumNo: number, revizyonNo: number, planNo?: number | null): Observable<any> {
    const suffix = planNo != null && planNo > 0 ? `?planNo=${planNo}` : '';
    return this.http.delete<any>(`${this.baseUrl}/series-requests/${id}/videos/${bolumNo}/revisions/${revizyonNo}${suffix}`);
  }

  // --- Studio Q&A (Çapraz Bölüm, Plan ve Kaynak Soru-Cevap) ---

  getStudioQaHistory(params: { egitimId?: string; contentRequestId?: string; bolumNo?: number | null; kaynakId?: string; kapsam: string }): Observable<any[]> {
    const q = new URLSearchParams();
    if (params.egitimId) q.set('egitimId', params.egitimId);
    if (params.contentRequestId) q.set('contentRequestId', params.contentRequestId);
    if (params.bolumNo != null) q.set('bolumNo', String(params.bolumNo));
    if (params.kaynakId) q.set('kaynakId', params.kaynakId);
    q.set('kapsam', params.kapsam);
    return this.http.get<any[]>(`${this.baseUrl}/qa/history?${q.toString()}`);
  }

  getStudioQaStatus(params: { egitimId?: string; contentRequestId?: string; bolumNo?: number | null; kaynakId?: string; kapsam: string }): Observable<{ isProcessing: boolean }> {
    const q = new URLSearchParams();
    if (params.egitimId) q.set('egitimId', params.egitimId);
    if (params.contentRequestId) q.set('contentRequestId', params.contentRequestId);
    if (params.bolumNo != null) q.set('bolumNo', String(params.bolumNo));
    if (params.kaynakId) q.set('kaynakId', params.kaynakId);
    q.set('kapsam', params.kapsam);
    return this.http.get<{ isProcessing: boolean }>(`${this.baseUrl}/qa/status?${q.toString()}`);
  }

  askStudioQuestion(payload: { egitimId?: string; contentRequestId?: string; bolumNo?: number | null; kaynakId?: string; kapsam: string; soru: string }): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/qa/ask`, payload);
  }

  applyStudioQaSuggestion(payload: { mesajId?: string; contentRequestId: string; hedefBolumNo: number; hedefAlan: string; eskiMetin: string; yeniMetin: string }): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/qa/apply-suggestion`, payload);
  }

  clearStudioQaHistory(params: { egitimId?: string; contentRequestId?: string; bolumNo?: number | null; kaynakId?: string; kapsam: string }): Observable<any> {
    const q = new URLSearchParams();
    if (params.egitimId) q.set('egitimId', params.egitimId);
    if (params.contentRequestId) q.set('contentRequestId', params.contentRequestId);
    if (params.bolumNo != null) q.set('bolumNo', String(params.bolumNo));
    if (params.kaynakId) q.set('kaynakId', params.kaynakId);
    q.set('kapsam', params.kapsam);
    return this.http.delete<any>(`${this.baseUrl}/qa/history?${q.toString()}`);
  }
}
