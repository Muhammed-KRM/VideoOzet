import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';

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
      .withUrl(environment.hubUrl)
      .withAutomaticReconnect()
      .build();

    this.addListeners();

    this.hubConnection
      .start()
      .then(() => console.log('SignalR connection started'))
      .catch(err => console.log('Error while starting connection: ' + err));
  }

  private addListeners() {
    if (!this.hubConnection) return;

    this.hubConnection.on('ReceiveProgress', (data) => {
      this.pipelineStageChanged$.next(data);
    });

    this.hubConnection.on('ContentReady', (data) => {
      this.contentGenerated$.next(data);
    });
  }
}
