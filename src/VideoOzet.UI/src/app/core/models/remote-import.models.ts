export type KaynakPlatformValue =
  | 'Yerel'
  | 'YouTube'
  | 'DirectUrl'
  | 'CustomApiManifest'
  | 0
  | 1
  | 2
  | 3;

export type UzaktanAlimModuValue =
  | 'AkilliTranskriptOncelikli'
  | 'DogrudanSesStt'
  | 0
  | 1;

export type VideoIslemDurumuValue =
  | 'Bekliyor'
  | 'SesAyristirmaBasladi'
  | 'SesAyristirmaTamamlandi'
  | 'SttBasladi'
  | 'SttTamamlandi'
  | 'OzetlemeBasladi'
  | 'OzetlemeTamamlandi'
  | 'IndekslemeBasladi'
  | 'Tamamlandi'
  | 'Hata'
  | 'Indiriliyor'
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8
  | 9
  | 10;

export type YouTubeDurationFilter = 'any' | 'medium' | 'long';

export interface YouTubeSearchItemDto {
  videoId: string;
  baslik: string;
  kanalAdi: string;
  kanalId?: string | null;
  thumbnailUrl: string;
  sureSaniye?: number | null;
  sureMetni: string;
  yayinTarihi?: string | null;
  goruntulenmeSayisi?: number | null;
  aciklama?: string | null;
  kaynakUrl: string;
}

export interface YouTubeSearchResponseDto {
  items: YouTubeSearchItemDto[];
  kullanilanKaynak: string;
  kotaAsildiMi: boolean;
  uyariMesaji?: string | null;
}

export interface YouTubeVideoDetailDto {
  videoId: string;
  baslik: string;
  aciklama: string;
  kanalAdi: string;
  kanalId?: string | null;
  thumbnailUrl: string;
  sureSaniye?: number | null;
  sureMetni: string;
  yayinTarihi?: string | null;
  goruntulenmeSayisi?: number | null;
  manuelAltyaziDilleri: string[];
  otomatikAltyaziDilleri: string[];
  ilgiliVideolar: YouTubeSearchItemDto[];
  kaynakUrl: string;
}

export interface RemoteVideoMetadataDto {
  disKaynakId: string;
  baslik: string;
  kaynakUrl: string;
  kaynakPlatform: KaynakPlatformValue;
  kanalAdi?: string | null;
  thumbnailUrl?: string | null;
  sureSaniye?: number | null;
  transcriptUrl?: string | null;
  altyaziMevcutMu: boolean;
  mevcutAltyaziDilleri: string[];
  zatenEgitimdeVarMi: boolean;
  drmKorumaliMi?: boolean;
  hataMesaji?: string | null;
}

export interface InspectRemoteUrlRequestDto {
  url: string;
  bearerToken?: string | null;
}

export interface InspectRemoteUrlResponseDto {
  items: RemoteVideoMetadataDto[];
  errors: string[];
}

export interface RemoteImportItemRequestDto {
  kaynakUrl: string;
  baslik?: string | null;
  disKaynakId?: string | null;
  kaynakPlatform: KaynakPlatformValue;
  kanalAdi?: string | null;
  thumbnailUrl?: string | null;
  sureSaniye?: number | null;
  transcriptUrl?: string | null;
}

export interface ImportRemoteVideosRequestDto {
  items: RemoteImportItemRequestDto[];
  alimModu: UzaktanAlimModuValue;
  kullanimTuru: number | string;
  indekslemeTuru: number | string;
  tercihEdilenDil?: string;
  bearerToken?: string | null;
}

export interface VideoItem {
  id: string;
  egitimId: string;
  baslik: string;
  dosyaYolu: string;
  transkript?: string | null;
  ozet?: string | null;
  islemDurumu: VideoIslemDurumuValue;
  kullanimTuru: number | string;
  indekslemeTuru: number | string;
  orijinalDil?: string | null;
  sttModel?: string | null;
  transkriptKaliteSkoru?: number | null;
  transkriptKaynakNotu?: string | null;
  kaynakPlatform?: KaynakPlatformValue;
  kaynakUrl?: string | null;
  disKaynakId?: string | null;
  kanalAdi?: string | null;
  thumbnailUrl?: string | null;
  alimModu?: UzaktanAlimModuValue;
  olusturmaTarihi: string;
  sonAsama?: string | null;
  mesaj?: string | null;
  hataMesaji?: string | null;
  yuzde?: number | null;
  mevcutAdim?: number | null;
  toplamAdim?: number | null;
  takiliMi?: boolean;
  takilmaNedeni?: string | null;
  sonGuncellemeTarihi?: string | null;
  tip?: 'video' | 'dokuman';
  durum?: string | null;
}

export interface ImportRemoteVideosResponseDto {
  eklenenVideolar: VideoItem[];
  atlananDuplicateIdler: string[];
  hatalar: string[];
  mesaj: string;
}
