var LibraryWebSocket = {
	$webSocketState: {
		instances: {},
		lastId: 0,
		onOpen: null,
		onMesssage: null,
		onError: null,
		onClose: null,
		debug: false
	},
	SetOnOpenDelegate: function(callback) {
		webSocketState.onOpen = callback;
	},
	SetOnMessageDelegate: function(callback) {
		webSocketState.onMessage = callback;
	},
	SetOnErrorDelegate: function(callback) {
		webSocketState.onError = callback;
	},
	SetOnCloseDelegate: function(callback) {
		webSocketState.onClose = callback;
	},
	CreateWebSocketInstance: function(url) {
		var urlStr = Pointer_stringify(url);
		var id = webSocketState.lastId++;

		webSocketState.instances[id] = {
			url: urlStr,
			ws: null
		};
		return id;
	},
	DestroyWebSocketInstance: function(instanceId) {
		var instance = webSocketState.instances[instanceId];

		if (!instance) return 0;
        
		if (instance.ws !== null && instance.ws.readyState < 2) 
            instance.ws.close();

		// Remove reference
		delete webSocketState.instances[instanceId];

		return 0;
	},
	Connect: function(instanceId) {
		var instance = webSocketState.instances[instanceId];

		if (!instance) return -1;
		if (instance.ws !== null) return -2;

		instance.ws = new WebSocket(instance.url);
		instance.ws.binaryType = 'arraybuffer';

		instance.ws.onopen = function() {
			if (webSocketState.debug) console.log("[MLAPI.WebSocket] Connected.");

			if (webSocketState.onOpen)
				Runtime.dynCall('vi', webSocketState.onOpen, [ instanceId ]);
		};

		instance.ws.onmessage = function(ev) {
			if (webSocketState.debug) console.log("[MLAPI.WebSocket] Received message:", ev.data);

			if (webSocketState.onMessage === null)
				return;

			if (ev.data instanceof ArrayBuffer) {
				var dataBuffer = new Uint8Array(ev.data);
				
				var buffer = _malloc(dataBuffer.length);
				HEAPU8.set(dataBuffer, buffer);

				try {
					Runtime.dynCall('viii', webSocketState.onMessage, [ instanceId, buffer, dataBuffer.length ]);
				} finally {
					_free(buffer);
				}
			}
		};

		instance.ws.onerror = function(ev) {
			if (webSocketState.debug) console.log("[MLAPI.WebSocket] Error occured.");

			if (webSocketState.onError) {
				var msg = "WebSocket error.";
				var msgBytes = lengthBytesUTF8(msg);
				var msgBuffer = _malloc(msgBytes + 1);
				stringToUTF8(msg, msgBuffer, msgBytes);

				try {
					Runtime.dynCall('vii', webSocketState.onError, [ instanceId, msgBuffer ]);
				} finally {
					_free(msgBuffer);
				}
			}
		};

		instance.ws.onclose = function(ev) {
			if (webSocketState.debug) console.log("[MLAPI.WebSocket] Closed.");

			if (webSocketState.onClose)
				Runtime.dynCall('vii', webSocketState.onClose, [ instanceId, ev.code ]);

			delete instance.ws;
		};

		return 0;
	},
	Close: function(instanceId, code, reasonPtr) {
		var instance = webSocketState.instances[instanceId];

		if (!instance) return -1;
		if (instance.ws === null) return -3;
		if (instance.ws.readyState === 2) return -4;
		if (instance.ws.readyState === 3) return -5;

		var reason = (reasonPtr ? Pointer_stringify(reasonPtr) : undefined);
		
		try {
			instance.ws.close(code, reason);
		} catch(err) {
			return -7;
		}

		return 0;
	},
	Send: function(instanceId, bufferPtr, offset, length) {
		var instance = webSocketState.instances[instanceId];

		if (!instance) return -1;
		if (instance.ws === null) return -3;
		if (instance.ws.readyState !== 1) return -6;

		instance.ws.send(HEAPU8.buffer.slice(bufferPtr + offset, bufferPtr + length - offset));

		return 0;
	},
	GetState: function(instanceId) {
		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws) return instance.ws.readyState;
		else return 3;
	}
};

autoAddDeps(LibraryWebSocket, '$webSocketState');
mergeInto(LibraryManager.library, LibraryWebSocket);