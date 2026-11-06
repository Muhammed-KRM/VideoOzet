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
            <!-- Sağlayıcı Seçimi (3 Seçenek: OpenRouter, Groq Cloud, Yerel Proxy) -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-400 mb-3">
                Aktif Yapay Zeka Sağlayıcısı (Sadece Buradan Girdiğiniz API Anahtarları Kullanılır)
              </label>
              <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <!-- Seçenek 1: OpenRouter API -->
                <button
                  type="button"
                  (click)="selectProvider('openrouter')"
                  class="flex flex-col items-start p-3.5 rounded-xl border text-left transition-all relative"
                  [ngClass]="form().activeProvider === 'openrouter'
                    ? 'border-cyan-400 bg-cyan-500/10 shadow-lg shadow-cyan-500/10'
                    : 'border-slate-700/80 bg-slate-800/50 hover:border-slate-600'"
                >
                  <div class="flex items-center justify-between w-full mb-1.5">
                    <span class="px-2 py-0.5 text-[11px] font-bold rounded-md bg-cyan-500/20 text-cyan-300 border border-cyan-500/30">
                      OpenRouter API
                    </span>
                    @if (form().activeProvider === 'openrouter') {
                      <span class="w-2.5 h-2.5 rounded-full bg-cyan-400 animate-pulse"></span>
                    }
                  </div>
                  <div class="font-semibold text-white text-xs">OpenRouter Modelleri</div>
                  <p class="text-[11px] text-slate-400 mt-1">
                    OpenRouter API anahtarınızla Gemini, Claude, GPT, DeepSeek modellerini kullanın.
                  </p>
                </button>

                <!-- Seçenek 2: Groq Cloud API -->
                <button
                  type="button"
                  (click)="selectProvider('groq')"
                  class="flex flex-col items-start p-3.5 rounded-xl border text-left transition-all relative"
                  [ngClass]="form().activeProvider === 'groq'
                    ? 'border-amber-400 bg-amber-500/10 shadow-lg shadow-amber-500/10'
                    : 'border-slate-700/80 bg-slate-800/50 hover:border-slate-600'"
                >
                  <div class="flex items-center justify-between w-full mb-1.5">
                    <span class="px-2 py-0.5 text-[11px] font-bold rounded-md bg-amber-500/20 text-amber-300 border border-amber-500/30">
                      Groq Cloud API
                    </span>
                    @if (form().activeProvider === 'groq') {
                      <span class="w-2.5 h-2.5 rounded-full bg-amber-400 animate-pulse"></span>
                    }
                  </div>
                  <div class="font-semibold text-white text-xs">Groq Whisper + LLM</div>
                  <p class="text-[11px] text-slate-400 mt-1">
                    Tek bir Groq anahtarıyla (gsk_...) hem ultra hızlı Whisper STT hem LLM özetleme çalıştırın.
                  </p>
                </button>

                <!-- Seçenek 3: Yerel / Özel Proxy -->
                <button
                  type="button"
                  (click)="selectProvider('antigravity')"
                  class="flex flex-col items-start p-3.5 rounded-xl border text-left transition-all relative"
                  [ngClass]="form().activeProvider === 'antigravity'
                    ? 'border-purple-500 bg-purple-500/10 shadow-lg shadow-purple-500/10'
                    : 'border-slate-700/80 bg-slate-800/50 hover:border-slate-600'"
                >
                  <div class="flex items-center justify-between w-full mb-1.5">
                    <span class="px-2 py-0.5 text-[11px] font-bold rounded-md bg-purple-500/20 text-purple-300 border border-purple-500/30">
                      Yerel / Özel Proxy
                    </span>
                    @if (form().activeProvider === 'antigravity') {
                      <span class="w-2.5 h-2.5 rounded-full bg-purple-400 animate-pulse"></span>
                    }
                  </div>
                  <div class="font-semibold text-white text-xs">Özel Proxy Sunucusu</div>
                  <p class="text-[11px] text-slate-400 mt-1">
                    Kendi bilgisayarınızdaki veya ağınızdaki OpenAI uyumlu proxy adresini kullanın.
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

                <!-- OpenRouter API Key -->
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
                    size="5"
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

            <!-- Groq Cloud API Ayarları -->
            @if (form().activeProvider === 'groq') {
              <div class="space-y-4 p-4 rounded-xl bg-slate-950/70 border border-slate-800">
                <div class="flex items-center justify-between">
                  <h3 class="text-sm font-semibold text-amber-400 flex items-center gap-2">
                    ⚡ Groq Cloud (Whisper STT + LLM) Yapılandırması
                  </h3>
                  <span class="text-[11px] text-slate-400">https://api.groq.com/openai/v1</span>
                </div>

                <div>
                  <label class="block text-xs font-medium text-slate-300 mb-1.5">Groq API Anahtarı (gsk_...)</label>
                  <div class="relative">
                    <input
                      [type]="showGroqKey() ? 'text' : 'password'"
                      [ngModel]="form().groqApiKey"
                      (ngModelChange)="updateField('groqApiKey', $event)"
                      placeholder="gsk_..."
                      class="w-full px-3.5 py-2.5 pr-20 rounded-lg bg-slate-900 border border-slate-700 text-sm text-white font-mono focus:outline-none focus:border-amber-400"
                    />
                    <button
                      type="button"
                      (click)="showGroqKey.set(!showGroqKey())"
                      class="absolute right-2 top-1/2 -translate-y-1/2 px-2.5 py-1 text-xs rounded bg-slate-800 text-slate-300 hover:text-white"
                    >
                      {{ showGroqKey() ? 'Gizle' : 'Göster' }}
                    </button>
                  </div>
                  <p class="text-[11px] text-slate-400 mt-1">
                    Bu anahtar hem videodan metin çıkarma (Whisper Large V3 Turbo) hem de özet/senaryo üretimi için kullanılır.
                  </p>
                </div>

                <div>
                  <label class="block text-xs font-medium text-slate-300 mb-1.5">Groq LLM Modeli</label>
                  <select
                    [ngModel]="form().groqModel"
                    (ngModelChange)="updateField('groqModel', $event)"
                    class="w-full px-3.5 py-2.5 rounded-lg bg-slate-900 border border-slate-700 text-sm text-white focus:outline-none focus:border-amber-400"
                  >
                    @for (m of settingsService.groqModels(); track m.id) {
                      <option [value]="m.id">{{ m.name }}</option>
                    }
                    @if (settingsService.groqModels().length === 0) {
                      <option value="openai/gpt-oss-120b">GPT-OSS 120B (Groq LPU - Önerilen)</option>
                      <option value="openai/gpt-oss-20b">GPT-OSS 20B (Groq LPU - Hızlı)</option>
                      <option value="qwen/qwen3.8-27b">Qwen 3.8 27B (Groq LPU)</option>
                    }
                  </select>
                </div>
              </div>
            }

            <!-- Antigravity / Yerel Proxy Ayarları -->
            @if (form().activeProvider === 'antigravity') {
              <div class="space-y-4 p-4 rounded-xl bg-slate-950/70 border border-slate-800">
                <div class="flex items-center justify-between">
                  <h3 class="text-sm font-semibold text-purple-300 flex items-center gap-2">
                    🚀 Yerel / Özel Proxy Yapılandırması
                  </h3>
                </div>

                <!-- Antigravity Model Seçimi -->
                <div>
                  <label class="block text-xs font-medium text-slate-300 mb-1.5">Kullanılacak Proxy Modeli</label>
                  <select
                    [ngModel]="form().antigravityProxyModel"
                    (ngModelChange)="updateField('antigravityProxyModel', $event)"
                    class="w-full px-3.5 py-2.5 rounded-lg bg-slate-900 border border-slate-700 text-sm text-white focus:outline-none focus:border-purple-500 cursor-pointer"
                  >
                    @if (!isModelInGroups(form().antigravityProxyModel)) {
                      <option [value]="form().antigravityProxyModel">📍 Özel / Seçili Model: {{ form().antigravityProxyModel }}</option>
                    }
                    @for (grp of groupedTaskModelOptions(); track grp.group) {
                      <optgroup [label]="grp.group" class="bg-slate-900 text-slate-200 font-semibold">
                        @for (m of grp.models; track m.id) {
                          <option [value]="m.id" class="bg-slate-950 text-slate-100 py-1 font-mono">
                            {{ m.name }} — [{{ m.id }}]
                          </option>
                        }
                      </optgroup>
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
                    <label class="block text-xs font-medium text-slate-300 mb-1">Proxy API Key</label>
                    <input
                      [type]="showProxyKey() ? 'text' : 'password'"
                      [ngModel]="form().antigravityProxyKey"
                      (ngModelChange)="updateField('antigravityProxyKey', $event)"
                      placeholder="sk-..."
                      class="w-full px-3 py-2 rounded-lg bg-slate-900 border border-slate-700 text-xs text-white font-mono focus:outline-none focus:border-purple-500"
                    />
                  </div>
                </div>
              </div>
            }

            <!-- OpenRouter veya Proxy seçiliyken Opsiyonel Groq Whisper STT Anahtarı Alanı -->
            @if (form().activeProvider !== 'groq') {
              <div class="p-4 rounded-xl bg-slate-950/50 border border-amber-500/20 space-y-2">
                <div class="flex items-center justify-between">
                  <label class="text-xs font-semibold text-amber-300 flex items-center gap-1.5">
                    🎙️ Ses Deşifre (Whisper STT) İçin Groq API Anahtarı (Opsiyonel ama Önerilen)
                  </label>
                  <span class="text-[10px] px-2 py-0.5 rounded bg-amber-500/10 text-amber-300 border border-amber-500/20">
                    Ultra Hızlı STT
                  </span>
                </div>
                <p class="text-[11px] text-slate-400">
                  Groq API anahtarınızı (<code class="text-amber-300">gsk_...</code>) girerseniz videoların sesinden metin çıkarma işlemi (STT) saniyeler içinde Groq Whisper ile yapılır. Boş bırakırsanız seçili sağlayıcı kullanılır.
                </p>
                <div class="relative">
                  <input
                    [type]="showGroqKey() ? 'text' : 'password'"
                    [ngModel]="form().groqApiKey"
                    (ngModelChange)="updateField('groqApiKey', $event)"
                    placeholder="gsk_... (İsteğe bağlı Groq API Anahtarı)"
                    class="w-full px-3.5 py-2 pr-20 rounded-lg bg-slate-900 border border-slate-700 text-xs text-white font-mono focus:outline-none focus:border-amber-400"
                  />
                  <button
                    type="button"
                    (click)="showGroqKey.set(!showGroqKey())"
                    class="absolute right-2 top-1/2 -translate-y-1/2 px-2 py-0.5 text-[11px] rounded bg-slate-800 text-slate-300 hover:text-white"
                  >
                    {{ showGroqKey() ? 'Gizle' : 'Göster' }}
                  </button>
                </div>
              </div>
            }

            <!-- Görev Bazlı Model Seçimi (Özetleme vs Video Planı/Senaryo vs Kalite Kontrol) -->
            <div class="p-4 rounded-xl bg-slate-950/80 border border-indigo-500/30 space-y-4">
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <span class="text-base">🎯</span>
                  <div>
                    <h4 class="text-xs font-bold text-indigo-300 uppercase tracking-wider">
                      İşlem Bazlı Model Dağılımı (Görev Ayrımı)
                    </h4>
                    <p class="text-[11px] text-slate-400">
                      Metin ve videoları özetlemek için hızlı bir model (örn: Gemini 3.8 Flash), video planı ve senaryo üretimi için güçlü bir model (örn: Claude 3.7 Sonnet veya Argon) seçebilirsiniz.
                    </p>
                  </div>
                </div>
              </div>

              <!-- Hızlı Hazır Şablonlar (Presets) -->
              <div class="flex flex-wrap gap-2 pt-1 border-t border-slate-800">
                <span class="text-[11px] text-slate-400 self-center">Hazır Şablonlar:</span>
                <button
                  type="button"
                  (click)="applyPreset('hybrid')"
                  class="px-2.5 py-1 text-[11px] rounded-lg bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 transition flex items-center gap-1.5"
                >
                  🚀 <b>Hibrit (Önerilen)</b>: Gemini Flash (Özet) + Claude 3.7 Sonnet (Plan)
                </button>
                <button
                  type="button"
                  (click)="applyPreset('argon')"
                  class="px-2.5 py-1 text-[11px] rounded-lg bg-purple-500/10 hover:bg-purple-500/20 text-purple-300 border border-purple-500/30 transition flex items-center gap-1.5"
                >
                  🧠 <b>Argon / Güçlü Muhakeme</b>: Gemini Flash (Özet) + Argon (Plan)
                </button>
                <button
                  type="button"
                  (click)="applyPreset('flash')"
                  class="px-2.5 py-1 text-[11px] rounded-lg bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-300 border border-cyan-500/30 transition flex items-center gap-1.5"
                >
                  ⚡ <b>Ultra Hızlı & Ekonomik</b>: Tüm İşlemler Gemini 3.8 Flash
                </button>
              </div>

              <!-- 1. Metin ve Video Özetleme Modeli -->
              <div class="p-3.5 rounded-xl bg-slate-900 border border-slate-800 space-y-2">
                <div class="flex items-center justify-between">
                  <label class="text-xs font-semibold text-slate-200 flex items-center gap-1.5">
                    📝 Metin & Video Doküman Özetleme Modeli
                  </label>
                  <span class="text-[10px] px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-300 border border-blue-500/20 font-medium">
                    Büyük Bağlam / Hızlı
                  </span>
                </div>
                <p class="text-[11px] text-slate-400">
                  Yüklenen doküman ve videoların ilk analiz ve özet çıkarımında kullanılır (Örn: Gemini Flash 3.8).
                </p>
                <div class="space-y-1.5">
                  <select
                    [ngModel]="activeSummaryModel()"
                    (ngModelChange)="updateSummaryModel($event)"
                    class="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700 text-xs text-blue-300 font-mono focus:outline-none focus:border-blue-400 cursor-pointer"
                  >
                    @if (!isModelInGroups(activeSummaryModel())) {
                      <option [value]="activeSummaryModel()">📍 Özel Model: {{ activeSummaryModel() }}</option>
                    }
                    @for (grp of groupedTaskModelOptions(); track grp.group) {
                      <optgroup [label]="grp.group" class="bg-slate-900 text-slate-200 font-semibold">
                        @for (m of grp.models; track m.id) {
                          <option [value]="m.id" class="bg-slate-950 text-slate-100 py-1 font-mono">
                            {{ m.name }} — [{{ m.id }}]
                          </option>
                        }
                      </optgroup>
                    }
                  </select>
                  <div class="flex items-center gap-2 text-[11px] text-slate-400">
                    <span class="whitespace-nowrap font-medium text-slate-400">Model ID:</span>
                    <input
                      type="text"
                      [ngModel]="activeSummaryModel()"
                      (ngModelChange)="updateSummaryModel($event)"
                      placeholder="Örn: gemini-3.8-flash-tiered"
                      class="flex-1 px-2.5 py-1 rounded bg-slate-950 border border-slate-800 text-[11px] text-blue-300 font-mono focus:outline-none focus:border-blue-400"
                    />
                  </div>
                </div>
              </div>

              <!-- 2. Video Planı & Senaryo Sentezi Modeli -->
              <div class="p-3.5 rounded-xl bg-slate-900 border border-purple-500/30 space-y-2">
                <div class="flex items-center justify-between">
                  <label class="text-xs font-semibold text-slate-200 flex items-center gap-1.5">
                    🎬 Video Planı, Senaryo & Revizyon Modeli (Synthesis)
                  </label>
                  <span class="text-[10px] px-2 py-0.5 rounded-full bg-purple-500/10 text-purple-300 border border-purple-500/20 font-medium">
                    Yüksek Muhakeme / Güçlü
                  </span>
                </div>
                <p class="text-[11px] text-slate-400">
                  Sahne kurgusu, kanca, promter anlatım metinleri ve revizyonlarda kullanılır (Örn: Argon veya Claude 3.7 Sonnet).
                </p>
                <div class="space-y-1.5">
                  <select
                    [ngModel]="activeVideoPlanModel()"
                    (ngModelChange)="updateVideoPlanModel($event)"
                    class="w-full px-3 py-2 rounded-lg bg-slate-950 border border-purple-500/50 text-xs text-purple-300 font-mono focus:outline-none focus:border-purple-400 cursor-pointer"
                  >
                    @if (!isModelInGroups(activeVideoPlanModel())) {
                      <option [value]="activeVideoPlanModel()">📍 Özel Model: {{ activeVideoPlanModel() }}</option>
                    }
                    @for (grp of groupedTaskModelOptions(); track grp.group) {
                      <optgroup [label]="grp.group" class="bg-slate-900 text-slate-200 font-semibold">
                        @for (m of grp.models; track m.id) {
                          <option [value]="m.id" class="bg-slate-950 text-slate-100 py-1 font-mono">
                            {{ m.name }} — [{{ m.id }}]
                          </option>
                        }
                      </optgroup>
                    }
                  </select>
                  <div class="flex items-center gap-2 text-[11px] text-slate-400">
                    <span class="whitespace-nowrap font-medium text-slate-400">Model ID:</span>
                    <input
                      type="text"
                      [ngModel]="activeVideoPlanModel()"
                      (ngModelChange)="updateVideoPlanModel($event)"
                      placeholder="Örn: argon veya claude-3-7-sonnet"
                      class="flex-1 px-2.5 py-1 rounded bg-slate-950 border border-slate-800 text-[11px] text-purple-300 font-mono focus:outline-none focus:border-purple-400"
                    />
                  </div>
                </div>
              </div>

              <!-- 3. Kalite Kontrol (QC) Modeli -->
              <div class="p-3.5 rounded-xl bg-slate-900 border border-slate-800 space-y-2">
                <div class="flex items-center justify-between">
                  <label class="text-xs font-semibold text-slate-200 flex items-center gap-1.5">
                    🛡️ Kalite Kontrol (QC Doğrulama) Modeli
                  </label>
                  <span class="text-[10px] px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-300 border border-emerald-500/20 font-medium">
                    Doğruluk Denetimi
                  </span>
                </div>
                <p class="text-[11px] text-slate-400">
                  Metindeki iddiaların kaynaklarla teyit edildiği aşamada çalışır (Örn: Gemini Flash 3.8).
                </p>
                <div class="space-y-1.5">
                  <select
                    [ngModel]="activeQcModel()"
                    (ngModelChange)="updateQcModel($event)"
                    class="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700 text-xs text-emerald-300 font-mono focus:outline-none focus:border-emerald-400 cursor-pointer"
                  >
                    @if (!isModelInGroups(activeQcModel())) {
                      <option [value]="activeQcModel()">📍 Özel Model: {{ activeQcModel() }}</option>
                    }
                    @for (grp of groupedTaskModelOptions(); track grp.group) {
                      <optgroup [label]="grp.group" class="bg-slate-900 text-slate-200 font-semibold">
                        @for (m of grp.models; track m.id) {
                          <option [value]="m.id" class="bg-slate-950 text-slate-100 py-1 font-mono">
                            {{ m.name }} — [{{ m.id }}]
                          </option>
                        }
                      </optgroup>
                    }
                  </select>
                  <div class="flex items-center gap-2 text-[11px] text-slate-400">
                    <span class="whitespace-nowrap font-medium text-slate-400">Model ID:</span>
                    <input
                      type="text"
                      [ngModel]="activeQcModel()"
                      (ngModelChange)="updateQcModel($event)"
                      placeholder="Örn: gemini-3.8-flash-tiered"
                      class="flex-1 px-2.5 py-1 rounded bg-slate-950 border border-slate-800 text-[11px] text-emerald-300 font-mono focus:outline-none focus:border-emerald-400"
                    />
                  </div>
                </div>
              </div>
            </div>

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
  readonly showGroqKey = signal(false);
  readonly showProxyKey = signal(false);
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
    antigravitySummaryModel: 'gemini-3.8-flash-tiered',
    antigravityVideoPlanModel: 'claude-3-7-sonnet-tiered',
    antigravityQcModel: 'gemini-3.8-flash-tiered'
  });

  readonly activeSummaryModel = computed(() => {
    const f = this.form();
    if (f.activeProvider === 'openrouter') return f.openRouterSummaryModel || f.openRouterModel;
    if (f.activeProvider === 'groq') return f.groqSummaryModel || f.groqModel;
    return f.antigravitySummaryModel || f.antigravityProxyModel;
  });

  readonly activeVideoPlanModel = computed(() => {
    const f = this.form();
    if (f.activeProvider === 'openrouter') return f.openRouterVideoPlanModel || f.openRouterModel;
    if (f.activeProvider === 'groq') return f.groqVideoPlanModel || f.groqModel;
    return f.antigravityVideoPlanModel || f.antigravityProxyModel;
  });

  readonly activeQcModel = computed(() => {
    const f = this.form();
    if (f.activeProvider === 'openrouter') return f.openRouterQcModel || f.openRouterModel;
    if (f.activeProvider === 'groq') return f.groqQcModel || f.groqModel;
    return f.antigravityQcModel || f.antigravityProxyModel;
  });

  updateSummaryModel(val: string): void {
    const p = this.form().activeProvider;
    if (p === 'openrouter') this.updateField('openRouterSummaryModel', val);
    else if (p === 'groq') this.updateField('groqSummaryModel', val);
    else this.updateField('antigravitySummaryModel', val);
  }

  updateVideoPlanModel(val: string): void {
    const p = this.form().activeProvider;
    if (p === 'openrouter') this.updateField('openRouterVideoPlanModel', val);
    else if (p === 'groq') this.updateField('groqVideoPlanModel', val);
    else this.updateField('antigravityVideoPlanModel', val);
  }

  updateQcModel(val: string): void {
    const p = this.form().activeProvider;
    if (p === 'openrouter') this.updateField('openRouterQcModel', val);
    else if (p === 'groq') this.updateField('groqQcModel', val);
    else this.updateField('antigravityQcModel', val);
  }

  applyPreset(preset: 'hybrid' | 'argon' | 'flash'): void {
    if (preset === 'hybrid') {
      this.updateField('openRouterSummaryModel', 'google/gemini-3.8-flash');
      this.updateField('openRouterVideoPlanModel', 'anthropic/claude-3.7-sonnet');
      this.updateField('openRouterQcModel', 'google/gemini-3.8-flash');
      this.updateField('antigravitySummaryModel', 'gemini-3.8-flash-tiered');
      this.updateField('antigravityVideoPlanModel', 'claude-3-7-sonnet-tiered');
      this.updateField('antigravityQcModel', 'gemini-3.8-flash-tiered');
    } else if (preset === 'argon') {
      this.updateField('openRouterSummaryModel', 'google/gemini-3.8-flash');
      this.updateField('openRouterVideoPlanModel', 'argon');
      this.updateField('openRouterQcModel', 'google/gemini-3.8-flash');
      this.updateField('antigravitySummaryModel', 'gemini-3.8-flash-tiered');
      this.updateField('antigravityVideoPlanModel', 'argon');
      this.updateField('antigravityQcModel', 'gemini-3.8-flash-tiered');
    } else if (preset === 'flash') {
      this.updateField('openRouterSummaryModel', 'google/gemini-3.8-flash');
      this.updateField('openRouterVideoPlanModel', 'google/gemini-3.8-flash');
      this.updateField('openRouterQcModel', 'google/gemini-3.8-flash');
      this.updateField('antigravitySummaryModel', 'gemini-3.8-flash-tiered');
      this.updateField('antigravityVideoPlanModel', 'gemini-3.8-flash-tiered');
      this.updateField('antigravityQcModel', 'gemini-3.8-flash-tiered');
    }
  }

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

  readonly groupedTaskModelOptions = computed<{ group: string; models: { id: string; name: string }[] }[]>(() => {
    const provider = this.form().activeProvider;

    if (provider === 'openrouter') {
      return [
        {
          group: '⭐ Öne Çıkan Modeller (Tavsiye Edilen)',
          models: [
            { id: 'google/gemini-3.8-flash', name: '⚡ Gemini Flash 3.8 (Ultra Hızlı / Geniş Bağlam)' },
            { id: 'argon', name: '🧠 Argon (Maksimum Muhakeme & Güçlü Planlama)' },
            { id: 'google/gemini-3.7-flash', name: '⚡ Gemini Flash 3.7' }
          ]
        },
        {
          group: '🧠 Anthropic Claude Modelleri',
          models: [
            { id: 'anthropic/claude-3.7-sonnet', name: 'Claude 3.7 Sonnet (Düşünce / Muhakeme)' },
            { id: 'anthropic/claude-3.5-sonnet', name: 'Claude 3.5 Sonnet (Yüksek Kalite)' },
            { id: 'anthropic/claude-3.5-haiku', name: 'Claude 3.5 Haiku (Hızlı & Ekonomik)' }
          ]
        },
        {
          group: '🤖 OpenAI Modelleri',
          models: [
            { id: 'openai/gpt-4o', name: 'GPT-4o (Amiral Gemisi Çok Yönlü)' },
            { id: 'openai/gpt-4o-mini', name: 'GPT-4o Mini (Hızlı & Hafif)' },
            { id: 'openai/o3-mini', name: 'o3-mini (İleri Muhakeme / STEM)' },
            { id: 'openai/o1', name: 'o1 (Derin Akıl Yürütme)' }
          ]
        },
        {
          group: '⚡ Google Gemini Modelleri',
          models: [
            { id: 'google/gemini-2.5-pro', name: 'Gemini 2.5 Pro (1M+ Token Bağlam)' },
            { id: 'google/gemini-2.5-flash', name: 'Gemini 2.5 Flash (Dengeli Hız)' },
            { id: 'google/gemini-1.5-pro', name: 'Gemini 1.5 Pro' },
            { id: 'google/gemini-1.5-flash', name: 'Gemini 1.5 Flash' }
          ]
        },
        {
          group: '🐋 DeepSeek Modelleri',
          models: [
            { id: 'deepseek/deepseek-r1', name: 'DeepSeek R1 (Açık Muhakeme)' },
            { id: 'deepseek/deepseek-chat', name: 'DeepSeek V3 (Güçlü Metin Sentezi)' },
            { id: 'deepseek/deepseek-r1:free', name: 'DeepSeek R1 (:free Ücretsiz)' },
            { id: 'deepseek/deepseek-chat-v3-0324:free', name: 'DeepSeek V3 (:free Ücretsiz)' }
          ]
        },
        {
          group: '🌐 Açık Kaynak & Diğer Modeller',
          models: [
            { id: 'meta-llama/llama-3.3-70b-instruct', name: 'Llama 3.3 70B Instruct' },
            { id: 'qwen/qwen-2.5-72b-instruct', name: 'Qwen 2.5 72B Instruct' },
            { id: 'mistralai/mistral-large-2411', name: 'Mistral Large' },
            { id: 'openai/gpt-oss-120b', name: 'GPT-OSS 120B (Groq LPU)' }
          ]
        }
      ];
    }

    if (provider === 'groq') {
      return [
        {
          group: '⭐ Öne Çıkan Modeller (Tavsiye Edilen)',
          models: [
            { id: 'gemini-3.8-flash', name: '⚡ Gemini Flash 3.8 (Yönlendirmeli)' },
            { id: 'argon', name: '🧠 Argon (Maksimum Muhakeme & Güçlü Planlama)' },
            { id: 'openai/gpt-oss-120b', name: '⚡ GPT-OSS 120B (Groq LPU - Önerilen)' }
          ]
        },
        {
          group: '⚡ Groq LPU Modelleri',
          models: [
            { id: 'llama-3.3-70b-versatile', name: 'Llama 3.3 70B Versatile (128K)' },
            { id: 'openai/gpt-oss-20b', name: 'GPT-OSS 20B (Ultra Hızlı)' },
            { id: 'qwen/qwen3.8-27b', name: 'Qwen 3.8 27B (Muhakeme Destekli)' }
          ]
        },
        {
          group: 'Diğer Modeller',
          models: [
            { id: 'claude-3-7-sonnet', name: 'Claude 3.7 Sonnet' },
            { id: 'gpt-4o', name: 'GPT-4o' }
          ]
        }
      ];
    }

    // antigravity (Yerel / Özel Proxy)
    return [
      {
        group: '⭐ Öne Çıkan Modeller (Tavsiye Edilen)',
        models: [
          { id: 'gemini-3.8-flash-tiered', name: '⚡ Gemini Flash 3.8 (Tiered / Ultra Hızlı)' },
          { id: 'argon', name: '🧠 Argon (Maksimum Muhakeme & Güçlü Planlama)' },
          { id: 'gemini-3.8-flash', name: '⚡ Gemini Flash 3.8 (Standart İsim)' }
        ]
      },
      {
        group: '🧠 Anthropic Claude Modelleri',
        models: [
          { id: 'claude-3-7-sonnet', name: 'Claude 3.7 Sonnet (Düşünce / Muhakeme)' },
          { id: 'claude-3-7-sonnet-tiered', name: 'Claude 3.7 Sonnet Tiered' },
          { id: 'claude-3-5-sonnet', name: 'Claude 3.5 Sonnet (Yüksek Kalite)' },
          { id: 'claude-3-5-haiku', name: 'Claude 3.5 Haiku (Hızlı & Ekonomik)' }
        ]
      },
      {
        group: '🤖 OpenAI Modelleri',
        models: [
          { id: 'gpt-4o', name: 'GPT-4o (Amiral Gemisi Çok Yönlü)' },
          { id: 'gpt-4o-mini', name: 'GPT-4o Mini (Hızlı & Hafif)' },
          { id: 'o3-mini', name: 'o3-mini (İleri Muhakeme / STEM)' },
          { id: 'o1', name: 'o1 (Derin Akıl Yürütme)' }
        ]
      },
      {
        group: '⚡ Google Gemini Modelleri',
        models: [
          { id: 'gemini-2.5-pro', name: 'Gemini 2.5 Pro (1M+ Token Bağlam)' },
          { id: 'gemini-2.5-flash', name: 'Gemini 2.5 Flash (Dengeli Hız)' },
          { id: 'gemini-1.5-pro', name: 'Gemini 1.5 Pro' },
          { id: 'gemini-1.5-flash', name: 'Gemini 1.5 Flash' }
        ]
      },
      {
        group: '🐋 DeepSeek Modelleri',
        models: [
          { id: 'deepseek-r1', name: 'DeepSeek R1 (Açık Muhakeme)' },
          { id: 'deepseek-chat', name: 'DeepSeek V3 (Güçlü Metin Sentezi)' }
        ]
      },
      {
        group: '🌐 Açık Kaynak & Diğer Modeller',
        models: [
          { id: 'llama-3.3-70b-instruct', name: 'Llama 3.3 70B Instruct' },
          { id: 'qwen-2.5-72b-instruct', name: 'Qwen 2.5 72B Instruct' },
          { id: 'openai/gpt-oss-120b', name: 'GPT-OSS 120B (Groq LPU)' },
          { id: 'mistral-large-2411', name: 'Mistral Large' }
        ]
      }
    ];
  });

  isModelInGroups(modelId: string | undefined): boolean {
    if (!modelId) return true;
    const clean = modelId.trim().toLowerCase();
    const groups = this.groupedTaskModelOptions();
    return groups.some(g => g.models.some(m => m.id.toLowerCase() === clean));
  }

  constructor() {
    effect(() => {
      const current = this.settingsService.settings();
      this.form.set({ ...current });
    }, { allowSignalWrites: true });
  }

  selectProvider(provider: 'openrouter' | 'groq' | 'antigravity'): void {
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
