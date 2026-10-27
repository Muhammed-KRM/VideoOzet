import { Component, inject, OnInit } from '@angular/core';
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
            <!-- Aktif Sağlayıcı Rozeti -->
            <button
              type="button"
              (click)="settingsService.openModal()"
              title="Aktif AI Sağlayıcısını Değiştir"
              class="hidden sm:flex items-center gap-2 px-3 py-1.5 rounded-full bg-slate-800/80 border border-slate-700/80 hover:border-slate-600 text-xs transition"
            >
              <span
                class="w-2 h-2 rounded-full animate-pulse"
                [ngClass]="settingsService.settings().activeProvider === 'openrouter' ? 'bg-cyan-400' : 'bg-purple-400'"
              ></span>
              <span class="text-slate-400">AI:</span>
              <span class="font-semibold text-white">
                {{ settingsService.settings().activeProvider === 'openrouter' ? 'OpenRouter' : 'Antigravity Proxy' }}
              </span>
              <span class="text-slate-500 font-mono text-[11px] max-w-[160px] truncate">
                ({{ settingsService.settings().activeProvider === 'openrouter'
                  ? settingsService.settings().openRouterModel
                  : settingsService.settings().antigravityProxyModel }})
              </span>
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
