import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class SignalRService {
  private hubConnection: signalR.HubConnection | undefined;
  
  public pipelineStageChanged$ = new Subject<any>();
  public contentGenerated$ = new Subject<any>();

  public startConnection() {
    // Aynı anda birden fazla bağlantı olmasını engellemek için
    if (this.hubConnection && this.hubConnection.state === signalR.HubConnectionState.Connected) {
      return;
    }

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('http://localhost:5001/pipeline-hub')
      .withAutomaticReconnect()
      .build();

    this.hubConnection
      .start()
      .then(() => console.log('SignalR connection started'))
      .catch(err => console.log('Error while starting connection: ' + err));

    this.addListeners();
  }

  private addListeners() {
    if (!this.hubConnection) return;

    this.hubConnection.on('StageChanged', (data) => {
      this.pipelineStageChanged$.next(data);
    });

    this.hubConnection.on('ContentGenerated', (data) => {
      this.contentGenerated$.next(data);
    });
  }
}
