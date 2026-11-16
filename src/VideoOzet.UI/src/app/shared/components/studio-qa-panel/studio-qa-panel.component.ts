import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { marked } from 'marked';
import { ApiService } from '../../../core/services/api.service';

@Component({
  selector: 'app-studio-qa-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="flex flex-col bg-slate-900/80 border border-slate-700/70 rounded-xl overflow-hidden shadow-xl">
      <!-- Üst Bilgi Çubuğu -->
      <div class="px-5 py-3.5 bg-slate-900 border-b border-slate-800 flex items-center justify-between gap-3 flex-wrap">
        <div>
          <h4 class="text-sm font-bold text-white flex items-center gap-2">
            <span>💬</span>
            <span>{{ baslik }}</span>
            <span class="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
              {{ kapsamEtiketi }}
            </span>
          </h4>
          <p *ngIf="altAciklama" class="text-xs text-slate-400 mt-0.5">{{ altAciklama }}</p>
        </div>

        <button
          *ngIf="messages.length > 0"
          type="button"
          (click)="clearHistory()"
          [disabled]="isAsking"
          class="px-2.5 py-1 text-[11px] font-medium text-slate-400 hover:text-rose-300 hover:bg-rose-950/40 rounded-lg border border-slate-700/60 transition-colors cursor-pointer disabled:opacity-50"
        >
          🗑️ Geçmişi Temizle
        </button>
      </div>

      <!-- Mesaj Geçmişi -->
      <div class="p-5 space-y-4 max-h-[520px] min-h-[240px] overflow-y-auto">
        <div *ngIf="isLoadingHistory" class="flex items-center justify-center py-10 text-slate-400 text-xs gap-2">
          <div class="animate-spin rounded-full h-5 w-5 border-2 border-slate-700 border-t-indigo-500"></div>
          <span>Sohbet geçmişi yükleniyor...</span>
        </div>

        <!-- Boş Durum & Örnek Sorular -->
        <div *ngIf="!isLoadingHistory && messages.length === 0" class="py-8 text-center space-y-4">
          <div class="w-12 h-12 rounded-full bg-indigo-500/15 border border-indigo-500/30 flex items-center justify-center mx-auto text-2xl">
            🧠
          </div>
          <div class="max-w-lg mx-auto space-y-1">
            <p class="text-sm font-semibold text-slate-200">Merak ettiğiniz her detayı sorabilirsiniz</p>
            <p class="text-xs text-slate-400 leading-relaxed">
              Sadece bir soru sorarsanız yalnızca net ve kanıtlı açıklama alırsınız. Değişiklik veya düzeltme önerisi isterseniz metne tek tıkla uygulanabilir öneri üretilir.
            </p>
          </div>

          <div *ngIf="ornekSorular.length > 0" class="flex flex-wrap justify-center gap-2 max-w-2xl mx-auto pt-2">
            <button
              *ngFor="let q of ornekSorular"
              type="button"
              (click)="usePromptChip(q)"
              class="px-3 py-1.5 rounded-lg bg-slate-800/90 hover:bg-indigo-950/60 text-slate-300 hover:text-indigo-200 border border-slate-700 hover:border-indigo-500/50 text-xs transition-all cursor-pointer text-left"
            >
              💡 {{ q }}
            </button>
          </div>
        </div>

        <!-- Mesaj Listesi -->
        <div *ngFor="let msg of messages" class="space-y-2">
          <!-- Kullanıcı Balonu -->
          <div *ngIf="msg.rol === 'user'" class="flex justify-end">
            <div class="max-w-[85%] px-4 py-2.5 rounded-2xl rounded-tr-sm bg-indigo-600 text-white text-sm whitespace-pre-wrap shadow">
              {{ msg.mesaj }}
            </div>
          </div>

          <!-- Asistan Balonu -->
          <div *ngIf="msg.rol === 'assistant'" class="flex justify-start">
            <div class="max-w-[95%] w-full bg-slate-800/95 border border-slate-700/80 rounded-2xl rounded-tl-sm p-4 space-y-3 shadow-lg">
              <div *ngIf="msg.kullanilanBaglamOzeti" class="flex items-center gap-1.5 text-[11px] text-indigo-300/90 bg-slate-900/80 px-2.5 py-1 rounded-md border border-slate-700/60 w-fit">
                <span>🔍 İncelenen Bağlam:</span>
                <span class="font-medium text-slate-300">{{ msg.kullanilanBaglamOzeti }}</span>
              </div>

              <div class="prose prose-invert prose-sm max-w-none text-slate-200 leading-relaxed" [innerHTML]="renderMarkdown(msg.mesaj)"></div>

              <!-- Cerrahi Düzeltme Önerisi Kartı (Yalnızca Öneri İstenmişse Gelir) -->
              <div *ngIf="msg.oneri" class="mt-3 p-4 rounded-xl bg-slate-900 border border-emerald-500/40 space-y-3">
                <div class="flex items-center justify-between gap-2 flex-wrap">
                  <span class="text-xs font-bold text-emerald-300 flex items-center gap-1.5">
                    <span>✨</span>
                    <span>Önerilen Metin Değişikliği (Bölüm {{ msg.oneri.hedefBolumNo || bolumNo }} · {{ msg.oneri.hedefAlan === 'ArastirmaOzeti' ? 'Araştırma Özeti' : 'Video Planı' }})</span>
                  </span>
                  <span *ngIf="msg.oneri.uygulandi" class="px-2.5 py-0.5 rounded-full bg-emerald-500/20 text-emerald-300 border border-emerald-500/40 text-[11px] font-bold">
                    ✓ Metne Uygulandı
                  </span>
                </div>

                <p *ngIf="msg.oneri.aciklama" class="text-xs text-slate-400">{{ msg.oneri.aciklama }}</p>

                <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-xs">
                  <div class="p-3 rounded-lg bg-rose-950/30 border border-rose-800/50 space-y-1">
                    <div class="font-bold text-rose-300 uppercase text-[10px]">Mevcut İfade</div>
                    <div class="text-rose-100/90 whitespace-pre-wrap font-mono text-[11px]">{{ msg.oneri.eskiMetin }}</div>
                  </div>
                  <div class="p-3 rounded-lg bg-emerald-950/30 border border-emerald-800/50 space-y-1">
                    <div class="font-bold text-emerald-300 uppercase text-[10px]">Önerilen Yeni İfade</div>
                    <div class="text-emerald-100/95 whitespace-pre-wrap font-mono text-[11px]">{{ msg.oneri.yeniMetin }}</div>
                  </div>
                </div>

                <div class="flex items-center justify-end gap-2 pt-1" *ngIf="!msg.oneri.uygulandi && contentRequestId">
                  <button
                    type="button"
                    (click)="applySuggestion(msg)"
                    [disabled]="applyingMessageId === msg.id"
                    class="px-4 py-1.5 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white text-xs font-bold rounded-lg shadow transition-all cursor-pointer flex items-center gap-1.5"
                  >
                    <span *ngIf="applyingMessageId === msg.id" class="animate-spin h-3.5 w-3.5 border-2 border-white border-t-transparent rounded-full"></span>
                    <span>{{ applyingMessageId === msg.id ? 'Uygulanıyor...' : '✅ Bu Değişikliği Metne Uygula' }}</span>
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Yanıt Bekleniyor -->
        <div *ngIf="isAsking" class="flex justify-start">
          <div class="px-4 py-3 rounded-2xl bg-slate-800 border border-slate-700 text-xs text-slate-300 flex items-center gap-2.5">
            <div class="animate-spin rounded-full h-4 w-4 border-2 border-indigo-400 border-t-transparent"></div>
            <span>İlgili bölümler, seri haritası ve kaynaklar inceleniyor...</span>
          </div>
        </div>

        <div *ngIf="errorMessage" class="p-3 rounded-xl bg-rose-950/50 border border-rose-700 text-rose-200 text-xs">
          ⚠️ {{ errorMessage }}
        </div>
      </div>

      <!-- Soru Giriş Alanı -->
      <div class="p-4 bg-slate-900 border-t border-slate-800">
        <div class="flex items-end gap-2">
          <textarea
            [(ngModel)]="questionText"
            (keydown.enter)="onEnterKey($event)"
            rows="2"
            [placeholder]="placeholder"
            [disabled]="isAsking"
            class="flex-1 px-3.5 py-2.5 bg-slate-950 border border-slate-700 focus:border-indigo-500 focus:outline-none rounded-xl text-sm text-white placeholder:text-slate-500 resize-none"
          ></textarea>
          <button
            type="button"
            (click)="sendQuestion()"
            [disabled]="isAsking || !questionText.trim()"
            class="px-4 py-2.5 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm font-bold rounded-xl shadow-lg shadow-indigo-600/20 transition-all cursor-pointer shrink-0 h-[42px] flex items-center gap-1.5"
          >
            <span>Gönder</span>
            <span>➤</span>
          </button>
        </div>
        <p class="text-[10px] text-slate-500 mt-1.5">
          Enter ile gönder · Shift+Enter ile alt satır. Sadece soru sorabilir ya da "şunu nasıl düzeltelim / öneri ver" diyerek değişiklik isteyebilirsiniz.
        </p>
      </div>
    </div>
  `
})
export class StudioQaPanelComponent implements OnChanges {
  @Input() kapsam: 'Bolum' | 'Plan' | 'Kaynak' = 'Bolum';
  @Input() egitimId = '';
  @Input() contentRequestId = '';
  @Input() bolumNo: number | null = null;
  @Input() kaynakId = '';
  @Input() baslik = 'Stüdyo Asistanı (Soru-Cevap)';
  @Input() altAciklama = '';
  @Input() placeholder = 'Bu döküman veya diğer bölümler hakkında bir soru sorun...';
  @Input() ornekSorular: string[] = [];

  @Output() suggestionApplied = new EventEmitter<void>();

  private apiService = inject(ApiService);

  messages: any[] = [];
  questionText = '';
  isLoadingHistory = false;
  isAsking = false;
  applyingMessageId: string | null = null;
  errorMessage = '';

  get kapsamEtiketi(): string {
    if (this.kapsam === 'Bolum') return 'Çapraz Bölüm & Kaynak Destekli';
    if (this.kapsam === 'Plan') return 'Seri Haritası & 46+ Kaynak Analizi';
    return 'Kaynak Tam Metin & Çapraz Kanıt';
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['contentRequestId'] || changes['bolumNo'] || changes['kaynakId'] || changes['kapsam'] || changes['egitimId']) {
      this.loadHistory();
    }
  }

  loadHistory() {
    if (!this.contentRequestId && !this.kaynakId && !this.egitimId) return;
    this.isLoadingHistory = true;
    this.errorMessage = '';

    this.apiService.getStudioQaHistory({
      egitimId: this.egitimId || undefined,
      contentRequestId: this.contentRequestId || undefined,
      bolumNo: this.bolumNo,
      kaynakId: this.kaynakId || undefined,
      kapsam: this.kapsam
    }).subscribe({
      next: (list) => {
        this.messages = Array.isArray(list) ? list : [];
        this.isLoadingHistory = false;
      },
      error: () => {
        this.isLoadingHistory = false;
      }
    });
  }

  usePromptChip(q: string) {
    this.questionText = q;
    this.sendQuestion();
  }

  onEnterKey(event: Event) {
    const kb = event as KeyboardEvent;
    if (!kb.shiftKey) {
      kb.preventDefault();
      this.sendQuestion();
    }
  }

  sendQuestion() {
    const q = this.questionText.trim();
    if (!q || this.isAsking) return;

    this.errorMessage = '';
    this.isAsking = true;
    this.questionText = '';

    this.messages.push({
      id: 'temp-' + Date.now(),
      rol: 'user',
      mesaj: q,
      olusturmaTarihi: new Date().toISOString()
    });

    this.apiService.askStudioQuestion({
      egitimId: this.egitimId || undefined,
      contentRequestId: this.contentRequestId || undefined,
      bolumNo: this.bolumNo,
      kaynakId: this.kaynakId || undefined,
      kapsam: this.kapsam,
      soru: q
    }).subscribe({
      next: (reply) => {
        this.isAsking = false;
        if (reply) {
          this.messages.push(reply);
        }
      },
      error: (err) => {
        this.isAsking = false;
        this.errorMessage = err?.error?.mesaj || 'Soru yanıtlanırken bir hata oluştu.';
      }
    });
  }

  applySuggestion(msg: any) {
    if (!msg?.oneri || !this.contentRequestId || this.applyingMessageId) return;
    const hedefBolumNo = msg.oneri.hedefBolumNo || this.bolumNo || 1;
    this.applyingMessageId = msg.id;
    this.errorMessage = '';

    this.apiService.applyStudioQaSuggestion({
      mesajId: msg.id,
      contentRequestId: this.contentRequestId,
      hedefBolumNo,
      hedefAlan: msg.oneri.hedefAlan || 'VideoPlani',
      eskiMetin: msg.oneri.eskiMetin,
      yeniMetin: msg.oneri.yeniMetin
    }).subscribe({
      next: () => {
        this.applyingMessageId = null;
        msg.oneri.uygulandi = true;
        this.suggestionApplied.emit();
      },
      error: (err) => {
        this.applyingMessageId = null;
        this.errorMessage = err?.error?.mesaj || 'Öneri metne uygulanamadı.';
      }
    });
  }

  clearHistory() {
    if (!confirm('Bu ekrandaki soru-cevap geçmişini temizlemek istiyor musunuz?')) return;
    this.apiService.clearStudioQaHistory({
      egitimId: this.egitimId || undefined,
      contentRequestId: this.contentRequestId || undefined,
      bolumNo: this.bolumNo,
      kaynakId: this.kaynakId || undefined,
      kapsam: this.kapsam
    }).subscribe({
      next: () => {
        this.messages = [];
      }
    });
  }

  renderMarkdown(text: string): string {
    if (!text) return '';
    return marked.parse(text) as string;
  }
}
