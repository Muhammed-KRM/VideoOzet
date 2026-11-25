import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AiProviderSettings {
  activeProvider: 'openrouter' | 'groq' | 'antigravity';
  openRouterApiKey: string;
  openRouterBaseUrl: string;
  openRouterModel: string;
  openRouterSummaryModel?: string;
  openRouterVideoPlanModel?: string;
  openRouterQcModel?: string;
  groqApiKey: string;
  groqModel: string;
  groqSummaryModel?: string;
  groqVideoPlanModel?: string;
  groqQcModel?: string;
  antigravityProxyUrl: string;
  antigravityProxyKey: string;
  antigravityProxyModel: string;
  antigravityProxySummaryModel?: string;
  antigravityProxyVideoPlanModel?: string;
  antigravityProxyQcModel?: string;
  // Geriye uyumluluk (eski alan adları)
  antigravitySummaryModel?: string;
  antigravityVideoPlanModel?: string;
  antigravityQcModel?: string;
  allowFallbackToSecondaryProvider?: boolean;
  youTubeApiKey?: string;
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
    activeProvider: 'openrouter',
    openRouterApiKey: '',
    openRouterBaseUrl: 'https://openrouter.ai/api/v1',
    openRouterModel: 'google/gemini-3.8-flash',
    openRouterSummaryModel: 'google/gemini-3.8-flash',
    openRouterVideoPlanModel: 'anthropic/claude-3.7-sonnet',
    openRouterQcModel: 'google/gemini-3.8-flash',
    groqApiKey: '',
    groqModel: 'openai/gpt-oss-120b',
    groqSummaryModel: 'openai/gpt-oss-120b',
    groqVideoPlanModel: 'openai/gpt-oss-120b',
    groqQcModel: 'openai/gpt-oss-120b',
    antigravityProxyUrl: 'http://localhost:8045/v1',
    antigravityProxyKey: '',
    antigravityProxyModel: 'gemini-3.8-flash-tiered',
    antigravityProxySummaryModel: 'gemini-3.8-flash-tiered',
    antigravityProxyVideoPlanModel: 'claude-3-7-sonnet-tiered',
    antigravityProxyQcModel: 'gemini-3.8-flash-tiered',
    antigravitySummaryModel: 'gemini-3.8-flash-tiered',
    antigravityVideoPlanModel: 'claude-3-7-sonnet-tiered',
    antigravityQcModel: 'gemini-3.8-flash-tiered',
    allowFallbackToSecondaryProvider: false,
    youTubeApiKey: ''
  });

  readonly openRouterModels = signal<ModelOption[]>([]);
  readonly groqModels = signal<ModelOption[]>([]);
  readonly antigravityModels = signal<ModelOption[]>([]);
  readonly isModalOpen = signal(false);
  readonly isLoading = signal(false);
  readonly isTesting = signal(false);
  readonly testResult = signal<TestConnectionResult | null>(null);

  normalizeSettings(raw: Partial<AiProviderSettings> | null | undefined): AiProviderSettings {
    const current = this.settings();
    const d = raw || {};
    const proxyModel = d.antigravityProxyModel || current.antigravityProxyModel || 'gemini-3.8-flash-tiered';
    const proxySummary =
      d.antigravityProxySummaryModel ||
      d.antigravitySummaryModel ||
      proxyModel;
    const proxyVideoPlan =
      d.antigravityProxyVideoPlanModel ||
      d.antigravityVideoPlanModel ||
      proxyModel;
    const proxyQc =
      d.antigravityProxyQcModel ||
      d.antigravityQcModel ||
      proxyModel;

    return {
      activeProvider: d.activeProvider || current.activeProvider || 'openrouter',
      openRouterApiKey: d.openRouterApiKey ?? current.openRouterApiKey ?? '',
      openRouterBaseUrl: d.openRouterBaseUrl || current.openRouterBaseUrl || 'https://openrouter.ai/api/v1',
      openRouterModel: d.openRouterModel || current.openRouterModel || 'google/gemini-3.8-flash',
      openRouterSummaryModel: d.openRouterSummaryModel || d.openRouterModel || current.openRouterSummaryModel || 'google/gemini-3.8-flash',
      openRouterVideoPlanModel: d.openRouterVideoPlanModel || d.openRouterModel || current.openRouterVideoPlanModel || 'anthropic/claude-3.7-sonnet',
      openRouterQcModel: d.openRouterQcModel || d.openRouterModel || current.openRouterQcModel || 'google/gemini-3.8-flash',
      groqApiKey: d.groqApiKey ?? current.groqApiKey ?? '',
      groqModel: d.groqModel || current.groqModel || 'openai/gpt-oss-120b',
      groqSummaryModel: d.groqSummaryModel || d.groqModel || current.groqSummaryModel || 'openai/gpt-oss-120b',
      groqVideoPlanModel: d.groqVideoPlanModel || d.groqModel || current.groqVideoPlanModel || 'openai/gpt-oss-120b',
      groqQcModel: d.groqQcModel || d.groqModel || current.groqQcModel || 'openai/gpt-oss-120b',
      antigravityProxyUrl: d.antigravityProxyUrl || current.antigravityProxyUrl || 'http://localhost:8045/v1',
      antigravityProxyKey: d.antigravityProxyKey ?? current.antigravityProxyKey ?? '',
      antigravityProxyModel: proxyModel,
      antigravityProxySummaryModel: proxySummary,
      antigravityProxyVideoPlanModel: proxyVideoPlan,
      antigravityProxyQcModel: proxyQc,
      antigravitySummaryModel: proxySummary,
      antigravityVideoPlanModel: proxyVideoPlan,
      antigravityQcModel: proxyQc,
      allowFallbackToSecondaryProvider: d.allowFallbackToSecondaryProvider ?? false,
      youTubeApiKey: d.youTubeApiKey ?? current.youTubeApiKey ?? ''
    };
  }

  openModal(): void {
    this.isModalOpen.set(true);
    this.testResult.set(null);
    this.loadSettings();
    this.loadModels('openrouter');
    this.loadModels('groq');
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
        this.settings.set(this.normalizeSettings(data));
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  loadModels(provider: 'openrouter' | 'groq' | 'antigravity'): void {
    this.http.get<ModelOption[]>(`${this.apiUrl}/models?provider=${provider}`).subscribe({
      next: (models) => {
        if (provider === 'openrouter') {
          this.openRouterModels.set(models);
        } else if (provider === 'groq') {
          this.groqModels.set(models);
        } else {
          this.antigravityModels.set(models);
        }
      },
      error: () => {}
    });
  }

  saveSettings(payload: AiProviderSettings): Observable<AiProviderSettings> {
    this.isLoading.set(true);
    const normalizedPayload = this.normalizeSettings(payload);
    return this.http.post<AiProviderSettings>(this.apiUrl, normalizedPayload).pipe(
      map((updated) => this.normalizeSettings(updated)),
      tap({
        next: (normalized) => {
          this.settings.set(normalized);
          this.isLoading.set(false);
        },
        error: () => this.isLoading.set(false)
      })
    );
  }

  testConnection(payload: AiProviderSettings): void {
    this.isTesting.set(true);
    this.testResult.set(null);
    const normalizedPayload = this.normalizeSettings(payload);
    this.http.post<TestConnectionResult>(`${this.apiUrl}/test`, normalizedPayload).subscribe({
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
