import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

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

  resetQueue(egitimId: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/videolar/reset-queue`, {});
  }

  deleteDokuman(egitimId: string, dokumanId: string): Observable<any> {
    return this.http.delete<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/dokumanlar/${dokumanId}`);
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

  getSeriesPlan(id: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/content-plans/${id}`);
  }

  generatePlanDraft(id: string, draftDto: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/content-plans/${id}/plans/draft`, draftDto);
  }

  updatePlanTexts(id: string, planNo: number, updates: any): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/content-plans/${id}/plans/${planNo}`, updates);
  }

  approvePlan(id: string, planNo: number): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/series-requests/${id}/plans/${planNo}/approve`, {});
  }

  getEpisodeDetails(id: string, planNo: number, bolumNo: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/series-requests/${id}/plans/${planNo}/videos/${bolumNo}`);
  }

  reviseEpisode(id: string, bolumNo: number, dto: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/series-requests/${id}/videos/${bolumNo}/revisions`, dto);
  }
}
