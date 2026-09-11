import { Component, Input, Output, EventEmitter, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-video-upload',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './video-upload.component.html'
})
export class VideoUploadComponent {
  @Input() egitimId!: string;
  @Output() uploadComplete = new EventEmitter<void>();
  isDragging = false;
  isUploading = false;
  
  currentFileIndex = 0;
  totalFiles = 0;
  currentFileName = '';
  progressPercent = 0;
  
  private apiService = inject(ApiService);

  onDragOver(event: DragEvent) {
    event.preventDefault();
    this.isDragging = true;
  }

  onDragLeave(event: DragEvent) {
    event.preventDefault();
    this.isDragging = false;
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    this.isDragging = false;
    if (event.dataTransfer?.files.length) {
      this.handleFiles(event.dataTransfer.files);
    }
  }

  onFileSelected(event: any) {
    if (event.target.files.length) {
      this.handleFiles(event.target.files);
    }
  }

  private async handleFiles(files: FileList) {
    if (files.length === 0) return;
    
    const validExtensions = ['.pdf', '.docx', '.pptx', '.txt', '.mp4', '.mov', '.avi', '.mkv'];
    const selectedFiles: File[] = [];

    for (let i = 0; i < files.length; i++) {
      const f = files[i];
      const lower = f.name.toLowerCase();
      const isValid = f.type.startsWith('video/') || validExtensions.some(ext => lower.endsWith(ext));
      if (isValid) {
        selectedFiles.push(f);
      }
    }

    if (selectedFiles.length === 0) {
      alert('Lütfen geçerli video, PDF, Word, PowerPoint (.pptx) veya metin dosyası seçin.');
      return;
    }

    this.isUploading = true;
    this.totalFiles = selectedFiles.length;
    this.currentFileIndex = 0;
    this.progressPercent = 0;

    let successCount = 0;
    let failCount = 0;

    for (let i = 0; i < selectedFiles.length; i++) {
      const file = selectedFiles[i];
      this.currentFileIndex = i + 1;
      this.currentFileName = file.name;
      this.progressPercent = Math.round(((i) / this.totalFiles) * 100);

      const formData = new FormData();
      formData.append('file', file);
      formData.append('baslik', file.name);

      try {
        await firstValueFrom(this.apiService.uploadFile(this.egitimId, formData));
        successCount++;
        this.uploadComplete.emit();
      } catch (err) {
        console.error(`Yükleme hatası: ${file.name}`, err);
        failCount++;
      }

      this.progressPercent = Math.round(((i + 1) / this.totalFiles) * 100);
    }

    this.isUploading = false;
    this.uploadComplete.emit();

    if (failCount > 0) {
      alert(`${successCount} dosya yüklendi. ${failCount} dosya yüklenemedi.`);
    }
  }
}
