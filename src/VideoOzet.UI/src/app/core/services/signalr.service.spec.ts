import { TestBed } from '@angular/core/testing';
import { SignalRService } from './signalr.service';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';

describe('SignalRService', () => {
  let service: SignalRService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SignalRService]
    });
    service = TestBed.inject(SignalRService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should build connection and add listeners on startConnection', () => {
    const mockConnection: any = {
      start: jasmine.createSpy('start').and.returnValue(Promise.resolve()),
      on: jasmine.createSpy('on'),
      state: signalR.HubConnectionState.Disconnected
    };

    spyOn(signalR.HubConnectionBuilder.prototype, 'withUrl').and.returnValue(signalR.HubConnectionBuilder.prototype);
    spyOn(signalR.HubConnectionBuilder.prototype, 'withAutomaticReconnect').and.returnValue(signalR.HubConnectionBuilder.prototype);
    spyOn(signalR.HubConnectionBuilder.prototype, 'build').and.returnValue(mockConnection);

    service.startConnection();

    (expect(signalR.HubConnectionBuilder.prototype.withUrl) as any).toHaveBeenCalledWith(environment.hubUrl);
    expect(mockConnection.on).toHaveBeenCalledWith('ReceiveProgress', jasmine.any(Function));
    expect(mockConnection.on).toHaveBeenCalledWith('ContentReady', jasmine.any(Function));
    expect(mockConnection.start).toHaveBeenCalled();
  });

  it('should pass data to pipelineStageChanged$ when ReceiveProgress is triggered', (done) => {
    const mockConnection: any = {
      start: jasmine.createSpy('start').and.returnValue(Promise.resolve()),
      on: jasmine.createSpy('on').and.callFake((eventName: string, callback: any) => {
        if (eventName === 'ReceiveProgress') {
          setTimeout(() => callback({ stage: 'STT' }), 10);
        }
      }),
      state: signalR.HubConnectionState.Disconnected
    };

    spyOn(signalR.HubConnectionBuilder.prototype, 'withUrl').and.returnValue(signalR.HubConnectionBuilder.prototype);
    spyOn(signalR.HubConnectionBuilder.prototype, 'withAutomaticReconnect').and.returnValue(signalR.HubConnectionBuilder.prototype);
    spyOn(signalR.HubConnectionBuilder.prototype, 'build').and.returnValue(mockConnection);

    service.pipelineStageChanged$.subscribe(data => {
      expect(data).toEqual({ stage: 'STT' });
      done();
    });

    service.startConnection();
  });
});
