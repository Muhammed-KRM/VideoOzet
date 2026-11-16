import { Component, inject, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { SettingsService } from '../../../core/services/settings.service';
import { SettingsModalComponent } from '../settings-modal/settings-modal.component';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterLink, SettingsModalComponent],
  template: `
    @if (!isLoginPage) {
      <header class="sticky top-0 z-40 bg-slate-900/90 backdrop-blur-md border-b border-slate-800 px-4 sm:px-6 lg:px-8 py-3">
        <div class="max-w-7xl mx-auto flex items-center justify-between">
          <!-- Sol: Logo & Ana Menü -->
          <div class="flex items-center gap-6">
            <a routerLink="/dashboard" class="flex items-center gap-3 group">
              <div class="w-9 h-9 rounded-xl bg-gradient-to-br from-blue-500 to-purple-600 flex items-center justify-center shadow-lg shadow-blue-500/20">
                <span class="text-white font-bold text-lg">V</span>
              </div>
              <div>
                <span class="text-base font-bold text-white group-hover:text-blue-400 transition-colors">
                  VideoÖzet AI
                </span>
                <span class="hidden sm:inline-block ml-2 px-2 py-0.5 text-[10px] font-semibold rounded-full bg-blue-500/10 text-blue-400 border border-blue-500/20">
                  Akademik & Seri Üretim
                </span>
              </div>
            </a>

            <nav class="hidden md:flex items-center gap-1">
              <a
                routerLink="/dashboard"
                class="px-3 py-1.5 rounded-lg text-sm font-medium text-slate-300 hover:text-white hover:bg-slate-800 transition-colors"
              >
                📚 Eğitimler
              </a>
            </nav>
          </div>

          <!-- Sağ: Aktif AI Sağlayıcı Durumu & API Ayarları Butonu -->
          <div class="flex items-center gap-3">
            <!-- Aktif Sağlayıcı & Görev Modeli Rozeti -->
            <button
              type="button"
              (click)="settingsService.openModal()"
              [title]="badgeTooltip()"
              class="hidden sm:flex items-center gap-2 px-3 py-1.5 rounded-full bg-slate-800/80 border border-slate-700/80 hover:border-slate-600 text-xs transition"
            >
              <span
                class="w-2 h-2 rounded-full animate-pulse shrink-0"
                [ngClass]="settingsService.settings().activeProvider === 'openrouter'
                  ? 'bg-cyan-400'
                  : settingsService.settings().activeProvider === 'groq'
                    ? 'bg-amber-400'
                    : 'bg-purple-400'"
              ></span>
              <span class="font-semibold text-white whitespace-nowrap">
                {{ providerLabel() }}:
              </span>
              <span class="px-1.5 py-0.5 rounded bg-purple-500/20 border border-purple-500/30 text-purple-200 font-mono text-[11px] font-semibold max-w-[200px] truncate">
                Plan: {{ activePlanModel() }}
              </span>
              @if (activeSummaryModel() !== activePlanModel()) {
                <span class="hidden lg:inline-block text-slate-400 font-mono text-[10px] max-w-[150px] truncate">
                  | Özet: {{ activeSummaryModel() }}
                </span>
              }
              @if (activeQcModel() !== activePlanModel() && activeQcModel() !== activeSummaryModel()) {
                <span class="hidden xl:inline-block text-slate-400 font-mono text-[10px] max-w-[140px] truncate">
                  | QC: {{ activeQcModel() }}
                </span>
              }
              @if (!settingsService.settings().allowFallbackToSecondaryProvider) {
                <span class="text-[10px] text-emerald-400" title="Kesin Model Kilidi Aktif (Yedek sağlayıcıya sessiz geçiş kapalı)">🔒</span>
              }
            </button>

            <!-- API Ayarları Butonu -->
            <button
              type="button"
              (click)="settingsService.openModal()"
              class="flex items-center gap-2 px-3.5 py-2 rounded-xl text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-100 border border-slate-700 hover:border-blue-500/50 transition shadow-sm"
            >
              <span>⚙️</span>
              <span>API Ayarları</span>
            </button>
          </div>
        </div>
      </header>
    }

    <app-settings-modal />
  `
})
export class NavbarComponent implements OnInit {
  readonly settingsService = inject(SettingsService);
  private readonly router = inject(Router);
  isLoginPage = false;

  readonly providerLabel = computed(() => {
    const p = this.settingsService.settings().activeProvider;
    if (p === 'openrouter') return 'OpenRouter';
    if (p === 'groq') return 'Groq Cloud';
    return 'Yerel Proxy';
  });

  readonly activePlanModel = computed(() => {
    const s = this.settingsService.settings();
    if (s.activeProvider === 'openrouter') {
      return s.openRouterVideoPlanModel || s.openRouterModel || 'anthropic/claude-3.7-sonnet';
    }
    if (s.activeProvider === 'groq') {
      return s.groqVideoPlanModel || s.groqModel || 'openai/gpt-oss-120b';
    }
    return (
      s.antigravityProxyVideoPlanModel ||
      s.antigravityVideoPlanModel ||
      s.antigravityProxyModel ||
      'claude-3-7-sonnet-tiered'
    );
  });

  readonly activeSummaryModel = computed(() => {
    const s = this.settingsService.settings();
    if (s.activeProvider === 'openrouter') {
      return s.openRouterSummaryModel || s.openRouterModel || 'google/gemini-3.8-flash';
    }
    if (s.activeProvider === 'groq') {
      return s.groqSummaryModel || s.groqModel || 'openai/gpt-oss-120b';
    }
    return (
      s.antigravityProxySummaryModel ||
      s.antigravitySummaryModel ||
      s.antigravityProxyModel ||
      'gemini-3.8-flash-tiered'
    );
  });

  readonly activeQcModel = computed(() => {
    const s = this.settingsService.settings();
    if (s.activeProvider === 'openrouter') {
      return s.openRouterQcModel || s.openRouterModel || 'google/gemini-3.8-flash';
    }
    if (s.activeProvider === 'groq') {
      return s.groqQcModel || s.groqModel || 'openai/gpt-oss-120b';
    }
    return (
      s.antigravityProxyQcModel ||
      s.antigravityQcModel ||
      s.antigravityProxyModel ||
      'gemini-3.8-flash-tiered'
    );
  });

  readonly badgeTooltip = computed(() => {
    const lockText = this.settingsService.settings().allowFallbackToSecondaryProvider
      ? 'Yedek sağlayıcıya geçiş açık'
      : 'Kesin Model Kilidi Aktif';
    return `Sağlayıcı: ${this.providerLabel()} | Plan/Senaryo: ${this.activePlanModel()} | Özet: ${this.activeSummaryModel()} | QC: ${this.activeQcModel()} (${lockText})`;
  });

  ngOnInit(): void {
    this.checkUrl(this.router.url);
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(e => {
        this.checkUrl(e.urlAfterRedirects);
      });

    if (localStorage.getItem('admin_token')) {
      this.settingsService.loadSettings();
    }
  }

  private checkUrl(url: string): void {
    const wasLogin = this.isLoginPage;
    this.isLoginPage = url.startsWith('/login');
    if (wasLogin && !this.isLoginPage && localStorage.getItem('admin_token')) {
      this.settingsService.loadSettings();
    }
  }
}
