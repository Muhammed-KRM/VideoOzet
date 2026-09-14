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

  uploadFile(egitimId: string, formData: FormData): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/upload`, formData);
  }

  uploadBatchFiles(egitimId: string, formData: FormData): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/dosyalar/upload-batch`, formData);
  }

  deleteVideo(egitimId: string, videoId: string): Observable<any> {
    return this.http.delete<any>(`${this.baseUrl}/egitimler/${egitimId}/videolar/${videoId}`);
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
}
