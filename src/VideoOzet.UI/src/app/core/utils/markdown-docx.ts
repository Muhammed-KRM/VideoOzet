import { marked, Token, Tokens } from 'marked';

/**
 * Markdown içeriğini GERÇEK Word (.docx) paragraflarına dönüştürür.
 *
 * Not: html-docx-js gibi kütüphaneler içeriği "altChunk" (gömülü HTML) olarak koyar;
 * Word Online, Google Docs, LibreOffice ve Windows önizleme bunu göstermez ve
 * belge BOŞ görünür. Bu yardımcı ise `docx` kütüphanesiyle yerel paragraflar üretir.
 */
export interface DocxSection {
  heading?: string;
  markdown: string;
}

export interface DocxExportOptions {
  title: string;
  subtitle?: string;
  sections: DocxSection[];
  pageBreakBetweenSections?: boolean;
}

interface RunStyle {
  bold?: boolean;
  italics?: boolean;
  strike?: boolean;
  code?: boolean;
  color?: string;
  underline?: boolean;
}

function decodeEntities(text: string): string {
  return (text || '')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&#039;/g, "'")
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&');
}

export async function markdownToDocxBlob(options: DocxExportOptions): Promise<Blob> {
  const docx = await import('docx');
  const {
    Document, Packer, Paragraph, TextRun, HeadingLevel, Table, TableRow, TableCell,
    WidthType, BorderStyle, ShadingType
  } = docx;

  const headingLevels = [
    HeadingLevel.HEADING_1, HeadingLevel.HEADING_2, HeadingLevel.HEADING_3,
    HeadingLevel.HEADING_4, HeadingLevel.HEADING_5, HeadingLevel.HEADING_6
  ];

  // ── Inline token → TextRun[] ──
  const inlineRuns = (tokens: Token[] | undefined, style: RunStyle = {}): any[] => {
    const runs: any[] = [];
    if (!tokens) return runs;

    for (const t of tokens as any[]) {
      switch (t.type) {
        case 'strong':
          runs.push(...inlineRuns(t.tokens, { ...style, bold: true }));
          break;
        case 'em':
          runs.push(...inlineRuns(t.tokens, { ...style, italics: true }));
          break;
        case 'del':
          runs.push(...inlineRuns(t.tokens, { ...style, strike: true }));
          break;
        case 'codespan':
          runs.push(makeRun(decodeEntities(t.text), { ...style, code: true }));
          break;
        case 'link':
          runs.push(...inlineRuns(t.tokens, { ...style, color: '1D4ED8', underline: true }));
          if (t.href && t.href !== t.text) {
            runs.push(makeRun(` (${t.href})`, { ...style, color: '64748B' }));
          }
          break;
        case 'br':
          runs.push(new TextRun({ text: '', break: 1 }));
          break;
        case 'text':
          if (t.tokens && t.tokens.length) {
            runs.push(...inlineRuns(t.tokens, style));
          } else {
            runs.push(...textWithBreaks(decodeEntities(t.text), style));
          }
          break;
        case 'escape':
          runs.push(makeRun(decodeEntities(t.text), style));
          break;
        case 'html':
          runs.push(makeRun(decodeEntities((t.text || '').replace(/<[^>]+>/g, '')), style));
          break;
        case 'image':
          runs.push(makeRun(`[Görsel: ${t.text || t.href}]`, { ...style, italics: true }));
          break;
        default:
          if (t.tokens) runs.push(...inlineRuns(t.tokens, style));
          else if (t.text) runs.push(makeRun(decodeEntities(t.text), style));
          else if (t.raw) runs.push(makeRun(t.raw, style));
      }
    }
    return runs;
  };

  const textWithBreaks = (text: string, style: RunStyle): any[] => {
    const parts = text.split('\n');
    return parts.map((p, i) => new TextRun({ ...runProps(style), text: p, break: i > 0 ? 1 : undefined }));
  };

  const runProps = (style: RunStyle) => ({
    bold: style.bold,
    italics: style.italics,
    strike: style.strike,
    color: style.color,
    underline: style.underline ? {} : undefined,
    font: style.code ? 'Consolas' : undefined,
    shading: style.code ? { type: ShadingType.CLEAR, color: 'auto', fill: 'F1F5F9' } : undefined
  });

  const makeRun = (text: string, style: RunStyle) => new TextRun({ ...runProps(style), text });

  // ── Block token → (Paragraph | Table)[] ──
  const blocks = (tokens: Token[], listLevel = 0, quote = false): any[] => {
    const out: any[] = [];

    for (const t of tokens as any[]) {
      switch (t.type) {
        case 'heading':
          out.push(new Paragraph({
            heading: headingLevels[Math.min(Math.max(t.depth, 1), 6) - 1],
            children: inlineRuns(t.tokens),
            spacing: { before: 240, after: 120 }
          }));
          break;

        case 'paragraph':
          out.push(new Paragraph({
            children: inlineRuns(t.tokens, quote ? { italics: true, color: '475569' } : {}),
            spacing: { after: 120 },
            indent: quote ? { left: 567 } : undefined
          }));
          break;

        case 'text':
          // Liste öğesi içindeki sıkı (tight) metin
          out.push(new Paragraph({ children: inlineRuns(t.tokens ?? [t]), spacing: { after: 60 } }));
          break;

        case 'list':
          out.push(...listBlocks(t as Tokens.List, listLevel));
          break;

        case 'blockquote':
          out.push(...blocks(t.tokens, listLevel, true));
          break;

        case 'code':
          for (const line of String(t.text || '').split('\n')) {
            out.push(new Paragraph({
              children: [makeRun(line || ' ', { code: true })],
              spacing: { after: 0 }
            }));
          }
          out.push(new Paragraph({ children: [] }));
          break;

        case 'hr':
          out.push(new Paragraph({
            children: [],
            border: { bottom: { color: 'CBD5E1', space: 1, style: BorderStyle.SINGLE, size: 6 } },
            spacing: { before: 120, after: 240 }
          }));
          break;

        case 'table':
          out.push(tableBlock(t as Tokens.Table));
          out.push(new Paragraph({ children: [] }));
          break;

        case 'html': {
          const plain = decodeEntities(String(t.text || '').replace(/<[^>]+>/g, '')).trim();
          if (plain) out.push(new Paragraph({ children: [makeRun(plain, {})] }));
          break;
        }

        case 'space':
          break;

        default:
          if (t.tokens) out.push(new Paragraph({ children: inlineRuns(t.tokens) }));
          else if (t.text) out.push(new Paragraph({ children: [makeRun(decodeEntities(t.text), {})] }));
      }
    }
    return out;
  };

  const listBlocks = (list: Tokens.List, level: number): any[] => {
    const out: any[] = [];
    let num = typeof list.start === 'number' ? list.start : 1;

    for (const item of list.items as any[]) {
      const children: Token[] = item.tokens || [];
      let firstDone = false;

      for (const child of children as any[]) {
        if (child.type === 'list') {
          out.push(...listBlocks(child, level + 1));
          continue;
        }
        if (child.type === 'space') continue;

        const runs = child.type === 'text' || child.type === 'paragraph'
          ? inlineRuns(child.tokens ?? [child])
          : null;

        if (runs && !firstDone) {
          if (list.ordered) {
            out.push(new Paragraph({
              children: [makeRun(`${num}. `, { bold: true }), ...runs],
              indent: { left: 360 * (level + 1), hanging: 300 },
              spacing: { after: 60 }
            }));
          } else {
            out.push(new Paragraph({
              children: runs,
              bullet: { level: Math.min(level, 8) },
              spacing: { after: 60 }
            }));
          }
          firstDone = true;
        } else if (runs) {
          out.push(new Paragraph({
            children: runs,
            indent: { left: 360 * (level + 1) + 360 },
            spacing: { after: 60 }
          }));
        } else {
          out.push(...blocks([child], level + 1));
        }
      }
      num++;
    }
    return out;
  };

  const tableBlock = (t: Tokens.Table): any => {
    const cell = (tokens: Token[], header: boolean) => new TableCell({
      children: [new Paragraph({ children: inlineRuns(tokens, header ? { bold: true } : {}) })],
      shading: header ? { type: ShadingType.CLEAR, color: 'auto', fill: 'E2E8F0' } : undefined,
      margins: { top: 60, bottom: 60, left: 100, right: 100 }
    });

    const rows = [
      new TableRow({ tableHeader: true, children: t.header.map((h: any) => cell(h.tokens, true)) }),
      ...t.rows.map((r: any[]) => new TableRow({ children: r.map((c: any) => cell(c.tokens, false)) }))
    ];

    return new Table({ rows, width: { size: 100, type: WidthType.PERCENTAGE } });
  };

  // ── Belge gövdesi ──
  const children: any[] = [
    new Paragraph({
      heading: HeadingLevel.TITLE,
      children: [new TextRun({ text: options.title, bold: true, color: '1E3A8A' })],
      spacing: { after: 80 }
    })
  ];

  if (options.subtitle) {
    children.push(new Paragraph({
      children: [new TextRun({ text: options.subtitle, color: '64748B', size: 20 })],
      border: { bottom: { color: '1E3A8A', space: 4, style: BorderStyle.SINGLE, size: 12 } },
      spacing: { after: 240 }
    }));
  }

  options.sections.forEach((section, idx) => {
    if (section.heading) {
      children.push(new Paragraph({
        heading: HeadingLevel.HEADING_1,
        children: [new TextRun({ text: section.heading, color: '1E3A8A' })],
        pageBreakBefore: idx > 0 && options.pageBreakBetweenSections !== false,
        spacing: { before: 240, after: 160 }
      }));
    }

    const md = (section.markdown || '').trim();
    if (!md) {
      children.push(new Paragraph({ children: [new TextRun({ text: 'İçerik bulunamadı.', italics: true, color: '94A3B8' })] }));
      return;
    }

    const tokens = marked.lexer(md, { gfm: true });
    children.push(...blocks(tokens));
  });

  const doc = new Document({
    creator: 'VideoOzet',
    title: options.title,
    styles: {
      default: {
        document: { run: { font: 'Calibri', size: 22 } }
      }
    },
    sections: [{
      properties: {
        page: { margin: { top: 1134, bottom: 1134, left: 1134, right: 1134 } }
      },
      children
    }]
  });

  return Packer.toBlob(doc);
}

/** QC listesini Markdown tablosuna çevirir (docx içinde gerçek tablo olur). */
export function qcToMarkdown(
  items: any[],
  summary: { skor?: number; desteklenen?: number; belirsiz?: number; desteklenmeyen?: number }
): string {
  const esc = (s: any) => String(s ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
  let md = `**Doğruluk Skoru:** %${summary.skor ?? 0}\n\n` +
    `- Desteklenen: ${summary.desteklenen ?? 0}\n` +
    `- Belirsiz: ${summary.belirsiz ?? 0}\n` +
    `- Desteklenmeyen: ${summary.desteklenmeyen ?? 0}\n\n`;

  if (!items || items.length === 0) {
    return md + '_Detaylı QC raporu bulunamadı._\n';
  }

  md += `| # | İddia | Durum | Açıklama |\n|---|---|---|---|\n`;
  items.forEach((it: any, i: number) => {
    md += `| ${i + 1} | ${esc(it.iddia)} | **${esc(it.durum)}** | ${esc(it.aciklama)} |\n`;
  });
  return md;
}

/** Tarayıcıda blob indirir. */
export function saveBlobAs(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1500);
}

// ──────────────────────────────────────────────────────────────────────────
// Prompter Konuşma Metni Ayrıştırma ve Dışa Aktarma Yardımcıları
// ──────────────────────────────────────────────────────────────────────────

export interface PrompterScene {
  sceneTitle: string;
  timeRange?: string;
  paragraphs: string[];
}

export function cleanPrompterParagraphs(lines: string[]): string[] {
  const stripped = lines.map(l => l.replace(/^\s*>\s?/, '').trimEnd());
  const paragraphs: string[] = [];
  let currentPara: string[] = [];

  for (const l of stripped) {
    if (l.trim() === '') {
      if (currentPara.length > 0) {
        paragraphs.push(currentPara.join(' ').trim());
        currentPara = [];
      }
    } else {
      currentPara.push(l.trim());
    }
  }
  if (currentPara.length > 0) {
    paragraphs.push(currentPara.join(' ').trim());
  }

  return paragraphs.map(p => {
    let clean = p;
    // 1. Dış tırnakları temizle
    clean = clean.replace(/^["“'„»]\s*/, '').replace(/\s*["”'«]$/, '');

    // 2. Prompt kalıntılarını temizle: (1. Paragraf: ...), 1. Paragraf:, Paragraf 1:
    clean = clean.replace(/^\(\s*\d+\.?\s*Paragraf\s*:\s*/i, '');
    clean = clean.replace(/^\d+\.?\s*Paragraf\s*:\s*/i, '');
    clean = clean.replace(/^Paragraf\s*\d+\s*:\s*/i, '');

    // 3. Eşleşen dış parantezleri temizle (örn. "(Merhaba dünya...)")
    if (clean.startsWith('(') && clean.endsWith(')')) {
      clean = clean.slice(1, -1).trim();
    } else if (clean.endsWith(')') && !clean.includes('(')) {
      clean = clean.slice(0, -1).trim();
    }

    // 4. Parantez içi temizlendikten sonra açığa çıkan tırnakları da temizle
    clean = clean.replace(/^["“'„»]\s*/, '').replace(/\s*["”'«]$/, '');

    return clean.trim();
  }).filter(p => p.length > 0);
}

function extractFallbackPrompter(markdown: string): PrompterScene[] {
  if (!markdown) return [];
  const lines = markdown.split(/\r?\n/);
  const scenes: PrompterScene[] = [];
  let currentSceneTitle: string | null = null;
  let currentTimeRange: string | null = null;
  let sceneLines: string[] = [];

  const finish = () => {
    if (currentSceneTitle && sceneLines.length > 0) {
      const nonBulletLines = sceneLines.filter(l => {
        const t = l.trim();
        if (!t) return true;
        if (t.startsWith('-') || t.startsWith('*') || t.startsWith('#')) return false;
        if (t.startsWith('---') || t.startsWith('===')) return false;
        return true;
      });
      const paragraphs = cleanPrompterParagraphs(nonBulletLines);
      if (paragraphs.length > 0) {
        scenes.push({
          sceneTitle: currentSceneTitle,
          timeRange: currentTimeRange || undefined,
          paragraphs
        });
      }
    }
    sceneLines = [];
  };

  for (const line of lines) {
    const trimmed = line.trim();
    const sceneMatch = trimmed.match(/^#{2,3}\s+(Sahne\b.*|\d+\.?\s*Sahne\b.*|(?:Giriş|Gelişme|Kapanış|Sonuç)\s*Sahnesi\b.*)/i);
    if (sceneMatch) {
      finish();
      currentSceneTitle = sceneMatch[1].trim();
      currentTimeRange = null;
      continue;
    }
    const timeMatch = trimmed.match(/^\*\*Zaman\s*Aral[ıi][gğ][ıi]:?\*\*\s*(.+)$/i);
    if (timeMatch) {
      currentTimeRange = timeMatch[1].trim().replace(/\*+/g, '');
      continue;
    }
    if (currentSceneTitle) {
      sceneLines.push(line);
    }
  }
  finish();
  return scenes;
}

export function extractPrompterScenes(videoPlanMarkdown: string): PrompterScene[] {
  if (!videoPlanMarkdown || !videoPlanMarkdown.trim()) return [];

  const lines = videoPlanMarkdown.split(/\r?\n/);
  const scenes: PrompterScene[] = [];

  let currentSceneTitle: string | null = null;
  let currentTimeRange: string | null = null;
  let inPrompter = false;
  let currentPrompterRawLines: string[] = [];

  const finishCurrentScene = () => {
    if (currentSceneTitle || currentPrompterRawLines.length > 0) {
      const paragraphs = cleanPrompterParagraphs(currentPrompterRawLines);
      if (paragraphs.length > 0) {
        scenes.push({
          sceneTitle: currentSceneTitle || `Sahne ${scenes.length + 1}`,
          timeRange: currentTimeRange || undefined,
          paragraphs
        });
      }
    }
    currentPrompterRawLines = [];
    inPrompter = false;
  };

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();

    // Sahne Başlığı
    const sceneMatch = trimmed.match(/^#{2,3}\s+(Sahne\b.*|\d+\.?\s*Sahne\b.*|(?:Giriş|Gelişme|Kapanış|Sonuç)\s*Sahnesi\b.*)/i);
    if (sceneMatch) {
      finishCurrentScene();
      currentSceneTitle = sceneMatch[1].trim();
      currentTimeRange = null;
      continue;
    }

    // Zaman Aralığı
    const timeMatch = trimmed.match(/^\*\*Zaman\s*Aral[ıi][gğ][ıi]:?\*\*\s*(.+)$/i);
    if (timeMatch) {
      currentTimeRange = timeMatch[1].trim().replace(/\*+/g, '');
      continue;
    }

    // Prompter Metni Başlığı
    const isPrompterHeading =
      /^#{3,4}\s*(?:2\.?\s*)?\[?Prompter/i.test(trimmed) ||
      /^\*\*Prompter\s*(?:Metni|Konuşma)?/i.test(trimmed) ||
      /^#{2,4}\s+.*Prompter\s*Metni/i.test(trimmed);

    if (isPrompterHeading) {
      inPrompter = true;
      continue;
    }

    // Başka bir bölüm başlığı veya ayraç geldiyse prompter'ı durdur
    if (inPrompter) {
      const isOtherHeading = /^#{2,4}\s+/.test(trimmed) && !isPrompterHeading;
      const isDivider = /^---+$/.test(trimmed) || /^===/.test(trimmed);

      if (isOtherHeading || isDivider) {
        inPrompter = false;
        continue;
      }

      currentPrompterRawLines.push(line);
    }
  }

  finishCurrentScene();

  // Hiç sahne bulunamadıysa fallback dene
  if (scenes.length === 0) {
    const fallback = extractFallbackPrompter(videoPlanMarkdown);
    if (fallback.length > 0) return fallback;
  }

  return scenes;
}

/**
 * Video planındaki yönetmen/kamera/kurgu yönergelerini ayıklayarak
 * SADECE prompterdan okunacak sahneleri ve konuşma metinlerini Markdown formatında döndürür.
 */
export function extractPrompterScript(videoPlanMarkdown: string, docTitle?: string): string {
  const scenes = extractPrompterScenes(videoPlanMarkdown);
  const titleHeader = docTitle
    ? `# ${docTitle} – Prompter Konuşma Metni\n\n`
    : '# Prompter Konuşma Metni\n\n';

  if (!scenes || scenes.length === 0) {
    return titleHeader + '_Bu video planında prompter konuşma metni bulunamadı._\n';
  }

  const sceneBlocks = scenes.map((scene) => {
    let block = `## ${scene.sceneTitle}\n`;
    if (scene.timeRange) {
      block += `**Zaman Aralığı:** ${scene.timeRange}\n\n`;
    } else {
      block += '\n';
    }
    block += scene.paragraphs.join('\n\n');
    return block;
  });

  return titleHeader + sceneBlocks.join('\n\n---\n\n') + '\n';
}

/**
 * Prompter cihazları ve teleprompter uygulamaları için en temiz düz metin (.txt) formatını üretir.
 * Markdown işaretleri olmadan, sahne blokları ve temiz konuşma paragrafları içerir.
 */
export function extractPrompterPlainText(videoPlanMarkdown: string, docTitle?: string): string {
  const scenes = extractPrompterScenes(videoPlanMarkdown);
  const sep = '='.repeat(50);
  const sceneSep = '-'.repeat(50);
  const title = (docTitle || 'VİDEO PLANI').toUpperCase();

  if (!scenes || scenes.length === 0) {
    return `${sep}\n${title}\nPROMPTER KONUŞMA METNİ\n${sep}\n\nBu video planında prompter konuşma metni bulunamadı.\n`;
  }

  let text = `${sep}\n${title}\nPROMPTER KONUŞMA METNİ\n${sep}\n\n`;

  scenes.forEach((scene, idx) => {
    text += `[${scene.sceneTitle.toUpperCase()}]\n`;
    if (scene.timeRange) {
      text += `Süre / Zaman Aralığı: ${scene.timeRange}\n`;
    }
    text += '\n';
    text += scene.paragraphs.join('\n\n');
    text += '\n\n';
    if (idx < scenes.length - 1) {
      text += `${sceneSep}\n\n`;
    }
  });

  return text;
}

/**
 * Sadece prompter metinlerini içeren temiz bir Word (.docx) belgesi üretir.
 */
export async function prompterToDocxBlob(
  videoPlanMarkdown: string,
  docTitle: string,
  dateStr?: string
): Promise<Blob> {
  const scenes = extractPrompterScenes(videoPlanMarkdown);
  const sections: DocxSection[] = scenes.map((scene) => ({
    heading: scene.sceneTitle + (scene.timeRange ? ` (${scene.timeRange})` : ''),
    markdown: scene.paragraphs.join('\n\n')
  }));

  if (sections.length === 0) {
    sections.push({
      heading: 'Prompter Metni',
      markdown: '_Bu video planında prompter konuşma metni bulunamadı._'
    });
  }

  return markdownToDocxBlob({
    title: `${docTitle} – Prompter Metni`,
    subtitle: `Sunucu Konuşma Metni · ${dateStr || new Date().toLocaleDateString('tr-TR')}`,
    sections,
    pageBreakBetweenSections: false
  });
}

