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

  getEgitimVideos(egitimId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/egitimler/${egitimId}/videolar`);
  }

  createContentRequest(egitimId: string, payload: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/egitimler/${egitimId}/content-requests`, payload);
  }

  getContentRequest(id: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/content-requests/${id}`);
  }
}
