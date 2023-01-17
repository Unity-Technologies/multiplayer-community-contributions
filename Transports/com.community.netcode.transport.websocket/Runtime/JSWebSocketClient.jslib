var LibraryWebSocket = {
  $state: {
    url: null,
    ws: null,
    debug: false,
    onOpen: null,
    onMessage: null,
    onError: null,
    onClose: null,
  },

  _SetUrl: function (urlPointer) {
    state.url = Pointer_stringify(urlPointer);
  },

  _SetOnOpen: function (callback) {
    state.onOpen = callback;
  },

  _SetOnMessage: function (callback) {
    state.onMessage = callback;
  },

  _SetOnError: function (callback) {
    state.onError = callback;
  },

  _SetOnClose: function (callback) {
    state.onClose = callback;
  },

  _Connect: function () {
    state.ws = new WebSocket(state.url);
    state.ws.binaryType = 'arraybuffer';

    state.ws.onopen = function () {
      if (state.debug) {
        console.log("[Netcode.WebSocket] Connected.");
      }

      if (state.onOpen) {
        Module['dynCall_v'](state.onOpen);
      }
    };

    state.ws.onmessage = function (ev) {
      if (state.debug) {
        console.log("[Netcode.WebSocket] Received message:", ev.data);
      }

      if (!state.onMessage) {
        return;
      }

      if (ev.data instanceof ArrayBuffer) {
        var dataBuffer = new Uint8Array(ev.data);

        var buffer = _malloc(dataBuffer.length);
        HEAPU8.set(dataBuffer, buffer);

        try {
          Module['dynCall_vii'](state.onMessage, buffer, dataBuffer.length);
        } finally {
          _free(buffer);
        }
      }
    };

    state.ws.onerror = function (ev) {
      if (state.debug) {
        console.log("[Netcode.WebSocket] Error occured.");
      }

      if (state.onError) {
        var msg = "WebSocket error.";
        var msgBytes = lengthBytesUTF8(msg);
        var msgBuffer = _malloc(msgBytes + 1);
        stringToUTF8(msg, msgBuffer, msgBytes);

        try {
          Module['dynCall_vi'](state.onError, msgBuffer)
        } finally {
          _free(msgBuffer);
        }
      }
    };

    state.ws.onclose = function (ev) {
      if (state.debug) {
        console.log("[Netcode.WebSocket] Closed.");
      }

      if (state.onClose) {
        Module['dynCall_vi'](state.onClose, ev.code)
      }
    };
  },

  _Close: function (code, reasonPointer) {
    if (!state.ws) return -3;
    if (state.ws.readyState === 2) return -4;
    if (state.ws.readyState === 3) return -5;

    var reason = (reasonPointer ? Pointer_stringify(reasonPointer) : undefined);

    try {
      state.ws.close(code, reason);
    } catch (err) {
      return -7;
    }
  },

  _Send: function (bufferPtr, offset, count) {
    if (!state.ws) return -3;
    if (state.ws.readyState !== 1) return -6;

    state.ws.send(HEAPU8.buffer.slice(bufferPtr + offset, bufferPtr + count - offset));
  },

  _GetState: function () {
    return state.ws ? state.ws.readyState : 3;
  }
};

autoAddDeps(LibraryWebSocket, '$state');
mergeInto(LibraryManager.library, LibraryWebSocket);
