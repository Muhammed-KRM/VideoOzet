import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AiProviderSettings {
  activeProvider: 'openrouter' | 'antigravity';
  openRouterApiKey: string;
  openRouterBaseUrl: string;
  openRouterModel: string;
  antigravityProxyUrl: string;
  antigravityProxyKey: string;
  antigravityProxyModel: string;
}

export interface TestConnectionResult {
  success: boolean;
  message: string;
  latencyMs: number;
}

export interface ModelOption {
  id: string;
  name: string;
  category: string;
  description: string;
}

@Injectable({
  providedIn: 'root'
})
export class SettingsService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/settings/ai-provider`;

  readonly settings = signal<AiProviderSettings>({
    activeProvider: 'antigravity',
    openRouterApiKey: '',
    openRouterBaseUrl: 'https://openrouter.ai/api/v1',
    openRouterModel: 'google/gemini-2.5-flash',
    antigravityProxyUrl: 'http://localhost:8045/v1',
    antigravityProxyKey: 'sk-ea6e0c519b12403784eb5cdf79ca2615',
    antigravityProxyModel: 'gemini-3.8-flash-tiered'
  });

  readonly openRouterModels = signal<ModelOption[]>([]);
  readonly antigravityModels = signal<ModelOption[]>([]);
  readonly isModalOpen = signal(false);
  readonly isLoading = signal(false);
  readonly isTesting = signal(false);
  readonly testResult = signal<TestConnectionResult | null>(null);

  openModal(): void {
    this.isModalOpen.set(true);
    this.testResult.set(null);
    this.loadSettings();
    this.loadModels('openrouter');
    this.loadModels('antigravity');
  }

  closeModal(): void {
    this.isModalOpen.set(false);
    this.testResult.set(null);
  }

  loadSettings(): void {
    this.isLoading.set(true);
    this.http.get<AiProviderSettings>(this.apiUrl).subscribe({
      next: (data) => {
        this.settings.set(data);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  loadModels(provider: 'openrouter' | 'antigravity'): void {
    this.http.get<ModelOption[]>(`${this.apiUrl}/models?provider=${provider}`).subscribe({
      next: (models) => {
        if (provider === 'openrouter') {
          this.openRouterModels.set(models);
        } else {
          this.antigravityModels.set(models);
        }
      },
      error: () => {}
    });
  }

  saveSettings(payload: AiProviderSettings): Observable<AiProviderSettings> {
    this.isLoading.set(true);
    return this.http.post<AiProviderSettings>(this.apiUrl, payload).pipe(
      tap({
        next: (updated) => {
          this.settings.set(updated);
          this.isLoading.set(false);
        },
        error: () => this.isLoading.set(false)
      })
    );
  }

  testConnection(payload: AiProviderSettings): void {
    this.isTesting.set(true);
    this.testResult.set(null);
    this.http.post<TestConnectionResult>(`${this.apiUrl}/test`, payload).subscribe({
      next: (res) => {
        this.testResult.set(res);
        this.isTesting.set(false);
      },
      error: (err) => {
        this.testResult.set({
          success: false,
          message: err?.error?.message || 'Sunucuya ulaşılamadı.',
          latencyMs: 0
        });
        this.isTesting.set(false);
      }
    });
  }
}
