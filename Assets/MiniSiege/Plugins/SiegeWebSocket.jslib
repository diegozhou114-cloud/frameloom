mergeInto(LibraryManager.library, {
  $SiegeSockets: { next: 1, items: {} },
  SiegeOpen__deps: ['$SiegeSockets'],
  SiegeOpen: function (urlPtr, receiverPtr) {
    var receiver = UTF8ToString(receiverPtr);
    var id = SiegeSockets.next++;
    try {
      var socket = new WebSocket(UTF8ToString(urlPtr));
      SiegeSockets.items[id] = socket;
      socket.onopen = function () {
        if (SiegeSockets.items[id]) SendMessage(receiver, 'OnSiegeOpen', '');
      };
      socket.onmessage = function (event) {
        if (SiegeSockets.items[id]) SendMessage(receiver, 'OnSiegeMessage', event.data);
      };
      socket.onerror = function () {
        if (SiegeSockets.items[id]) SendMessage(receiver, 'OnSiegeError', '连接失败，请检查服务器地址');
      };
      socket.onclose = function () {
        if (SiegeSockets.items[id]) SendMessage(receiver, 'OnSiegeError', '连接已关闭，请重新连接');
      };
      return id;
    } catch (error) {
      SendMessage(receiver, 'OnSiegeError', String(error));
      return 0;
    }
  },
  SiegeSend__deps: ['$SiegeSockets'],
  SiegeSend: function (id, messagePtr) {
    var socket = SiegeSockets.items[id];
    if (socket && socket.readyState === WebSocket.OPEN) socket.send(UTF8ToString(messagePtr));
  },
  SiegeClose__deps: ['$SiegeSockets'],
  SiegeClose: function (id) {
    var socket = SiegeSockets.items[id];
    delete SiegeSockets.items[id];
    if (socket) socket.close();
  }
});
