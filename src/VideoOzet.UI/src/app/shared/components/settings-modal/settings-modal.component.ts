import { Component, inject, signal, effect, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, AiProviderSettings, ModelOption } from '../../../core/services/settings.service';

@Component({
  selector: 'app-settings-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    @if (settingsService.isModalOpen()) {
      <div class="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-black/75 backdrop-blur-sm animate-fade-in">
        <div class="relative w-full max-w-2xl rounded-2xl bg-slate-900 border border-slate-700/80 shadow-2xl overflow-hidden text-slate-100">
          <!-- Üst Başlık -->
          <div class="flex items-center justify-between px-6 py-4 border-b border-slate-800 bg-slate-950/60">
            <div class="flex items-center gap-3">
              <div class="w-10 h-10 rounded-xl bg-gradient-to-br from-blue-500/20 to-purple-500/20 border border-blue-500/30 flex items-center justify-center text-xl">
                ⚙️
              </div>
              <div>
                <h2 class="text-lg font-bold text-white">Sistem & Yapay Zeka (API) Ayarları</h2>
                <p class="text-xs text-slate-400">Video özetleme, ses deşifre (STT) ve senaryo üretimi için aktif AI sağlayıcısını seçin</p>
              </div>
            </div>
            <button (click)="close()" class="p-2 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition">
              ✕
            </button>
          </div>

          <!-- İçerik -->
          <div class="p-6 space-y-6 max-h-[80vh] overflow-y-auto">
            <!-- Sağlayıcı Seçimi (2 Seçenek: Antigravity vs OpenRouter) -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-400 mb-3">
                Aktif Yapay Zeka Sağlayıcısı
              </label>
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <!-- Seçenek 1: Mevcut Antigravity Proxy -->
                <button
                  type="button"
                  (click)="selectProvider('antigravity')"
                  class="flex flex-col items-start p-4 rounded-xl border text-left transition-all relative"
                  [ngClass]="form().activeProvider === 'antigravity'
                    ? 'border-purple-500 bg-purple-500/10 shadow-lg shadow-purple-500/10'
                    : 'border-slate-700/80 bg-slate-800/50 hover:border-slate-600'"
                >
                  <div class="flex items-center justify-between w-full mb-2">
                    <span class="px-2.5 py-0.5 text-xs font-bold rounded-md bg-purple-500/20 text-purple-300 border border-purple-500/30">
                      Antigravity AI (Mevcut)
                    </span>
                    @if (form().activeProvider === 'antigravity') {
                      <span class="w-2.5 h-2.5 rounded-full bg-purple-400 animate-pulse"></span>
                    }
                  </div>
                  <div class="font-semibold text-white text-sm">Yerel Antigravity Proxy</div>
                  <p class="text-xs text-slate-400 mt-1">
                    Mevcut yerel proxy ({{ form().antigravityProxyUrl }}) üzerinden Gemini 3.8 Flash / Claude modellerini kullanır.
                  </p>
                </button>

                <!-- Seçenek 2: OpenRouter API -->
                <button
                  type="button"
                  (click)="selectProvider('openrouter')"
                  class="flex flex-col items-start p-4 rounded-xl border text-left transition-all relative"
                  [ngClass]="form().activeProvider === 'openrouter'
                    ? 'border-cyan-400 bg-cyan-500/10 shadow-lg shadow-cyan-500/10'
                    : 'border-slate-700/80 bg-slate-800/50 hover:border-slate-600'"
                >
                  <div class="flex items-center justify-between w-full mb-2">
                    <span class="px-2.5 py-0.5 text-xs font-bold rounded-md bg-cyan-500/20 text-cyan-300 border border-cyan-500/30">
                      OpenRouter API
                    </span>
                    @if (form().activeProvider === 'openrouter') {
                      <span class="w-2.5 h-2.5 rounded-full bg-cyan-400 animate-pulse"></span>
                    }
                  </div>
                  <div class="font-semibold text-white text-sm">OpenRouter Bulut Modelleri</div>
                  <p class="text-xs text-slate-400 mt-1">
                    Başka bilgisayarda veya bulutta OpenRouter API anahtarınızla istediğiniz modeli seçip çalıştırın.
                  </p>
                </button>
              </div>
            </div>

            <!-- OpenRouter Ayarları -->
            @if (form().activeProvider === 'openrouter') {
              <div class="space-y-4 p-4 rounded-xl bg-slate-950/70 border border-slate-800">
                <div class="flex items-center justify-between">
                  <h3 class="text-sm font-semibold text-cyan-400 flex items-center gap-2">
                    🔑 OpenRouter Yapılandırması
                  </h3>
                  <span class="text-[11px] text-slate-400">Endpoint: {{ form().openRouterBaseUrl }}</span>
                </div>

                <!-- API Key -->
                <div>
                  <label class="block text-xs font-medium text-slate-300 mb-1.5">OpenRouter API Anahtarı</label>
                  <div class="relative">
                    <input
                      [type]="showApiKey() ? 'text' : 'password'"
                      [ngModel]="form().openRouterApiKey"
                      (ngModelChange)="updateField('openRouterApiKey', $event)"
                      placeholder="sk-or-v1-..."
                      class="w-full px-3.5 py-2.5 pr-20 rounded-lg bg-slate-900 border border-slate-700 text-sm text-white font-mono focus:outline-none focus:border-cyan-400"
                    />
                    <button
                      type="button"
                      (click)="showApiKey.set(!showApiKey())"
                      class="absolute right-2 top-1/2 -translate-y-1/2 px-2.5 py-1 text-xs rounded bg-slate-800 text-slate-300 hover:text-white"
                    >
                      {{ showApiKey() ? 'Gizle' : 'Göster' }}
                    </button>
                  </div>
                </div>

                <!-- Model Seçimi -->
                <div class="space-y-2.5">
                  <div class="flex items-center justify-between">
                    <label class="block text-xs font-medium text-slate-300">
                      OpenRouter Model Seçimi
                      <span class="ml-1 text-[11px] text-cyan-400 font-normal">
                        ({{ filteredOpenRouterModels().length }} / {{ settingsService.openRouterModels().length }} model)
                      </span>
                    </label>
                    <button
                      type="button"
                      (click)="settingsService.loadModels('openrouter')"
                      class="text-[11px] text-cyan-400 hover:underline"
                    >
                      ↻ Listeyi Yenile
                    </button>
                  </div>

                  <!-- Hızlı Arama & Kategori Filtresi -->
                  <div class="flex flex-col sm:flex-row gap-2">
                    <div class="relative flex-1">
                      <input
                        type="text"
                        [ngModel]="modelSearchQuery()"
                        (ngModelChange)="modelSearchQuery.set($event)"
                        placeholder="🔍 Model ara (örn: gemini, claude, gpt-4o, deepseek, free)..."
                        class="w-full px-3 py-2 rounded-lg bg-slate-900 border border-slate-700 text-xs text-white focus:outline-none focus:border-cyan-400"
                      />
                      @if (modelSearchQuery()) {
                        <button
                          type="button"
                          (click)="modelSearchQuery.set('')"
                          class="absolute right-2 top-1/2 -translate-y-1/2 text-xs text-slate-400 hover:text-white"
                        >
                          ✕
                        </button>
                      }
                    </div>
                  </div>

                  <!-- Kategori Chipleri -->
                  <div class="flex flex-wrap gap-1.5">
                    @for (cat of categoryChips; track cat.key) {
                      <button
                        type="button"
                        (click)="selectedCategoryChip.set(cat.key)"
                        class="px-2.5 py-1 rounded-md text-[11px] font-medium transition border"
                        [ngClass]="selectedCategoryChip() === cat.key
                          ? 'bg-cyan-500/20 text-cyan-300 border-cyan-400/50'
                          : 'bg-slate-900 text-slate-400 border-slate-800 hover:text-white'"
                      >
                        {{ cat.label }}
                      </button>
                    }
                  </div>

                  <!-- Kategorize Model Listesi -->
                  <select
                    [ngModel]="form().openRouterModel"
                    (ngModelChange)="updateField('openRouterModel', $event)"
                    size="6"
                    class="w-full px-3 py-2 rounded-lg bg-slate-900 border border-slate-700 text-xs text-white focus:outline-none focus:border-cyan-400 font-mono"
                  >
                    @for (group of groupedOpenRouterModels(); track group.category) {
                      <optgroup [label]="group.category + ' (' + group.models.length + ')'">
                        @for (m of group.models; track m.id) {
                          <option [value]="m.id" class="py-1">
                            {{ m.name }} — [{{ m.id }}]
                          </option>
                        }
                      </optgroup>
                    }
                  </select>

                  <!-- Özel Model ID veya Link Girişi -->
                  <div>
                    <label class="block text-[11px] text-slate-400 mb-1">
                      Seçili Model ID (veya doğrudan OpenRouter model ID / URL yapıştırın):
                    </label>
                    <input
                      type="text"
                      [ngModel]="form().openRouterModel"
                      (ngModelChange)="onCustomModelInput($event)"
                      placeholder="Örn: google/gemini-2.5-flash veya anthropic/claude-3.7-sonnet"
                      class="w-full px-3.5 py-2 rounded-lg bg-slate-900 border border-cyan-500/40 text-xs text-cyan-300 font-mono focus:outline-none focus:border-cyan-400"
                    />
                  </div>
                </div>
              </div>
            }

            <!-- Antigravity Proxy Ayarları -->
            @if (form().activeProvider === 'antigravity') {
              <div class="space-y-4 p-4 rounded-xl bg-slate-950/70 border border-slate-800">
                <div class="flex items-center justify-between">
                  <h3 class="text-sm font-semibold text-purple-300 flex items-center gap-2">
                    🚀 Antigravity AI Proxy Yapılandırması
                  </h3>
                  <span class="px-2 py-0.5 text-[11px] rounded bg-emerald-500/10 text-emerald-300 border border-emerald-500/20">
                    Varsayılan Sistem
                  </span>
                </div>

                <!-- Antigravity Model Seçimi -->
                <div>
                  <label class="block text-xs font-medium text-slate-300 mb-1.5">Kullanılacak Antigravity Modeli</label>
                  <select
                    [ngModel]="form().antigravityProxyModel"
                    (ngModelChange)="updateField('antigravityProxyModel', $event)"
                    class="w-full px-3.5 py-2.5 rounded-lg bg-slate-900 border border-slate-700 text-sm text-white focus:outline-none focus:border-purple-500"
                  >
                    @for (m of settingsService.antigravityModels(); track m.id) {
                      <option [value]="m.id">{{ m.name }}</option>
                    }
                  </select>
                </div>

                <!-- Özel Model ID Girişi -->
                <div>
                  <label class="block text-[11px] text-slate-400 mb-1">
                    Model ID (elle değiştirmek isterseniz):
                  </label>
                  <input
                    type="text"
                    [ngModel]="form().antigravityProxyModel"
                    (ngModelChange)="updateField('antigravityProxyModel', $event)"
                    placeholder="gemini-3.8-flash-tiered"
                    class="w-full px-3 py-2 rounded-lg bg-slate-900 border border-slate-700 text-xs text-purple-300 font-mono focus:outline-none focus:border-purple-500"
                  />
                </div>

                <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <div>
                    <label class="block text-xs font-medium text-slate-300 mb-1">Proxy Endpoint URL</label>
                    <input
                      type="text"
                      [ngModel]="form().antigravityProxyUrl"
                      (ngModelChange)="updateField('antigravityProxyUrl', $event)"
                      placeholder="http://localhost:8045/v1"
                      class="w-full px-3 py-2 rounded-lg bg-slate-900 border border-slate-700 text-xs text-white font-mono focus:outline-none focus:border-purple-500"
                    />
                  </div>
                  <div>
                    <label class="block text-xs font-medium text-slate-300 mb-1">Proxy API Key (Çoklu anahtar için virgül kullanın)</label>
                    <input
                      type="text"
                      [ngModel]="form().antigravityProxyKey"
                      (ngModelChange)="updateField('antigravityProxyKey', $event)"
                      class="w-full px-3 py-2 rounded-lg bg-slate-900 border border-slate-700 text-xs text-white font-mono focus:outline-none focus:border-purple-500"
                    />
                  </div>
                </div>
              </div>
            }

            <!-- Bağlantı Test Sonucu -->
            @if (settingsService.testResult(); as result) {
              <div
                class="p-3.5 rounded-xl border text-xs flex items-center justify-between"
                [ngClass]="result.success
                  ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-300'
                  : 'bg-rose-500/10 border-rose-500/30 text-rose-300'"
              >
                <div class="flex items-center gap-2">
                  <span class="text-base">{{ result.success ? '✅' : '⚠️' }}</span>
                  <span>{{ result.message }}</span>
                </div>
                <span class="font-mono text-[11px] opacity-75">{{ result.latencyMs }} ms</span>
              </div>
            }

            @if (saveMessage()) {
              <div class="p-3 rounded-xl bg-cyan-500/10 border border-cyan-500/30 text-cyan-300 text-xs flex items-center gap-2">
                <span>💾</span>
                <span>{{ saveMessage() }}</span>
              </div>
            }
          </div>

          <!-- Alt Aksiyonlar -->
          <div class="flex items-center justify-between px-6 py-4 border-t border-slate-800 bg-slate-950/60">
            <button
              type="button"
              (click)="testConnection()"
              [disabled]="settingsService.isTesting()"
              class="px-4 py-2 rounded-lg text-xs font-semibold border border-slate-700 bg-slate-800 text-slate-200 hover:bg-slate-700 transition disabled:opacity-50"
            >
              {{ settingsService.isTesting() ? 'Test Ediliyor...' : '🔌 Bağlantıyı Test Et' }}
            </button>

            <div class="flex items-center gap-3">
              <button
                type="button"
                (click)="close()"
                class="px-4 py-2 rounded-lg text-xs font-medium text-slate-400 hover:text-white transition"
              >
                Kapat
              </button>
              <button
                type="button"
                (click)="save()"
                [disabled]="settingsService.isLoading()"
                class="px-5 py-2 rounded-lg text-xs font-bold bg-gradient-to-r from-blue-600 to-purple-600 hover:from-blue-500 hover:to-purple-500 text-white shadow-lg shadow-blue-500/20 transition disabled:opacity-50"
              >
                {{ settingsService.isLoading() ? 'Kaydediliyor...' : '✓ Kaydet ve Etkinleştir' }}
              </button>
            </div>
          </div>
        </div>
      </div>
    }
  `
})
export class SettingsModalComponent {
  readonly settingsService = inject(SettingsService);

  readonly showApiKey = signal(false);
  readonly saveMessage = signal('');
  readonly modelSearchQuery = signal('');
  readonly selectedCategoryChip = signal('ALL');

  readonly categoryChips = [
    { key: 'ALL', label: 'Tümü' },
    { key: 'Google Gemini', label: '⚡ Gemini' },
    { key: 'Ücretsiz Modeller (:free)', label: '🎁 Ücretsiz (:free)' },
    { key: 'Anthropic Claude', label: '🧠 Claude' },
    { key: 'OpenAI GPT', label: '🤖 OpenAI' },
    { key: 'DeepSeek', label: '🐋 DeepSeek' },
    { key: 'Qwen', label: '🌐 Qwen' }
  ];

  readonly form = signal<AiProviderSettings>({
    activeProvider: 'antigravity',
    openRouterApiKey: '',
    openRouterBaseUrl: 'https://openrouter.ai/api/v1',
    openRouterModel: 'google/gemini-2.5-flash',
    antigravityProxyUrl: 'http://localhost:8045/v1',
    antigravityProxyKey: 'sk-ea6e0c519b12403784eb5cdf79ca2615',
    antigravityProxyModel: 'gemini-3.8-flash-tiered'
  });

  readonly filteredOpenRouterModels = computed(() => {
    const all = this.settingsService.openRouterModels();
    const q = this.modelSearchQuery().trim().toLowerCase();
    const chip = this.selectedCategoryChip();

    return all.filter(m => {
      const matchesChip = chip === 'ALL' || m.category === chip;
      const matchesQuery = !q || m.id.toLowerCase().includes(q) || m.name.toLowerCase().includes(q) || m.category.toLowerCase().includes(q);
      return matchesChip && matchesQuery;
    });
  });

  readonly groupedOpenRouterModels = computed(() => {
    const models = this.filteredOpenRouterModels();
    const map = new Map<string, ModelOption[]>();
    for (const m of models) {
      const list = map.get(m.category) ?? [];
      list.push(m);
      map.set(m.category, list);
    }
    return Array.from(map.entries()).map(([category, items]) => ({ category, models: items }));
  });

  constructor() {
    effect(() => {
      const current = this.settingsService.settings();
      this.form.set({ ...current });
    }, { allowSignalWrites: true });
  }

  selectProvider(provider: 'openrouter' | 'antigravity'): void {
    this.form.update(f => ({ ...f, activeProvider: provider }));
    this.settingsService.testResult.set(null);
    this.saveMessage.set('');
  }

  updateField<K extends keyof AiProviderSettings>(key: K, value: AiProviderSettings[K]): void {
    this.form.update(f => ({ ...f, [key]: value }));
    this.saveMessage.set('');
  }

  onCustomModelInput(raw: string): void {
    let cleaned = raw.trim();
    if (cleaned.startsWith('https://openrouter.ai/')) {
      try {
        const url = new URL(cleaned);
        let path = url.pathname.replace(/^\/+|\/+$/g, '');
        if (path.startsWith('models/')) {
          path = path.substring('models/'.length);
        }
        if (path) cleaned = path;
      } catch {}
    }
    this.updateField('openRouterModel', cleaned);
  }

  testConnection(): void {
    this.settingsService.testConnection(this.form());
  }

  save(): void {
    this.settingsService.saveSettings(this.form()).subscribe({
      next: () => {
        this.saveMessage.set('Ayarlar kaydedildi! Yeni AI işlemleri seçtiğiniz sağlayıcı ve model üzerinden çalışacak.');
        setTimeout(() => {
          this.saveMessage.set('');
          this.close();
        }, 1200);
      }
    });
  }

  close(): void {
    this.settingsService.closeModal();
  }
}
