mergeInto(LibraryManager.library, {
  TW_WebSocketConnect: function (urlPtr, objectNamePtr) {
    if (!window.TankWarfareSockets) {
      window.TankWarfareSockets = { nextId: 1, values: {} };
    }

    var url = UTF8ToString(urlPtr);
    var objectName = UTF8ToString(objectNamePtr);
    var id = window.TankWarfareSockets.nextId++;

    try {
      var socket = new WebSocket(url);
      window.TankWarfareSockets.values[id] = socket;
      socket.onopen = function () { SendMessage(objectName, 'OnWebSocketOpen', String(id)); };
      socket.onmessage = function (event) { SendMessage(objectName, 'OnWebSocketMessage', String(event.data)); };
      socket.onerror = function () { SendMessage(objectName, 'OnWebSocketError', 'Ошибка WebSocket'); };
      socket.onclose = function () {
        delete window.TankWarfareSockets.values[id];
        SendMessage(objectName, 'OnWebSocketClose', String(id));
      };
      return id;
    } catch (error) {
      SendMessage(objectName, 'OnWebSocketError', String(error));
      return -1;
    }
  },

  TW_WebSocketSend: function (id, messagePtr) {
    var registry = window.TankWarfareSockets;
    var socket = registry && registry.values[id];
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(UTF8ToString(messagePtr));
    }
  },

  TW_WebSocketClose: function (id) {
    var registry = window.TankWarfareSockets;
    var socket = registry && registry.values[id];
    if (socket) socket.close(1000, 'client closed');
  }
});
