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
  public contentProgress$ = new Subject<any>();
  public contentError$ = new Subject<any>();
  public seriesPlanGenerated$ = new Subject<any>();
  public seriesVideoGenerated$ = new Subject<any>();
  public topicAnalysisCompleted$ = new Subject<any>();
  public seriesPlanCompleted$ = new Subject<any>();

  public startConnection() {
    if (this.hubConnection &&
      (this.hubConnection.state === signalR.HubConnectionState.Connected ||
       this.hubConnection.state === signalR.HubConnectionState.Connecting ||
       this.hubConnection.state === signalR.HubConnectionState.Reconnecting)) {
      return;
    }

    if (!this.hubConnection) {
      this.hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(environment.hubUrl)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 15000, 30000, 30000, 30000, 30000, 30000])
        .build();

      this.addListeners();
      this.hubConnection.onclose(() => {
        setTimeout(() => this.startConnection(), 5000);
      });
    }

    this.hubConnection
      .start()
      .then(() => console.log('SignalR connection started'))
      .catch(err => {
        console.log('Error while starting connection: ' + err);
        setTimeout(() => this.startConnection(), 5000);
      });
  }

  public async ensureConnected(): Promise<boolean> {
    if (!this.hubConnection) {
      this.startConnection();
      return false;
    }
    if (this.hubConnection.state === signalR.HubConnectionState.Connected) {
      return true;
    }
    if (this.hubConnection.state === signalR.HubConnectionState.Disconnected) {
      try {
        await this.hubConnection.start();
        return true;
      } catch {
        return false;
      }
    }
    return false;
  }

  private addListeners() {
    if (!this.hubConnection) return;

    this.hubConnection.on('ReceiveProgress', (data) => {
      this.pipelineStageChanged$.next(data);
    });

    this.hubConnection.on('ContentReady', (data) => {
      this.contentGenerated$.next(data);
    });

    this.hubConnection.on('ContentProgress', (data) => {
      this.contentProgress$.next(data);
    });

    this.hubConnection.on('ContentError', (data) => {
      this.contentError$.next(data);
    });

    this.hubConnection.on('SeriesPlanGenerated', (data) => {
      this.seriesPlanGenerated$.next(data);
    });

    this.hubConnection.on('SeriesVideoGenerated', (data) => {
      this.seriesVideoGenerated$.next(data);
    });

    this.hubConnection.on('TopicAnalysisCompleted', (data) => {
      this.topicAnalysisCompleted$.next(data);
    });

    this.hubConnection.on('SeriesPlanCompleted', (data) => {
      this.seriesPlanCompleted$.next(data);
    });
  }
}
