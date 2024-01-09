let ws = null;
let keepAliveInterval = null;
let reconnectInterval = null;
let reconnectAttempts = 0;
const KEEP_ALIVE_MSG = 'keep-alive';
const KEEP_ALIVE_INTERVAL = 30000; // Отправлять keep-alive сообщение каждые 30 секунд
const RECONNECT_ATTEMPT_INTERVAL = 5000; // Попытка переподключения каждые 5 секунд
const MAX_RECONNECT_ATTEMPTS = 12; // Максимум 12 попыток в течение минуты (60 / 5)

    const targetTabs = new Set(); // Используем Set для хранения уникальных идентификаторов вкладок

    function updateTabDiscardable(tabId, nonDiscardable) {
        if ((ws && ws.readyState === WebSocket.OPEN) || !nonDiscardable) {
            chrome.tabs.update(tabId, { autoDiscardable: !nonDiscardable });
        }
    }

    function setAllTabsDiscardable(nonDiscardable) {
        targetTabs.forEach(tabId => updateTabDiscardable(tabId, nonDiscardable));
    }

    chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
        if (tab.url && tab.url.includes("https://chat.openai.com/")) {
            targetTabs.add(tabId); // Добавляем вкладку в список
            updateTabDiscardable(tabId, true); // Включаем или выключаем autoDiscardable
        }
    });

    chrome.tabs.onRemoved.addListener((tabId, removeInfo) => {
        targetTabs.delete(tabId); // Удаляем вкладку из списка при ее закрытии
        if (targetTabs.size === 0) {
            SetConnectionStatus('Searching for tab'); // Обновляем статус, если больше нет целевых вкладок
        }
    });

    // Слушатель для создания новых вкладок
    chrome.tabs.onCreated.addListener((tab) => {
        if (tab.url && tab.url.includes("https://chat.openai.com/")) {
            updateTabDiscardable(tab.id, true)
        }
    });

    function SetConnectionStatus(status) {
        console.log(`Setting connection status to: ${status}`);  // Добавлено логирование
        chrome.storage.local.set({ ConnectionStatus: status });
    }

    function connectWebSocket() {

        if (targetTabs.size > 0 && (!ws || ws.readyState === WebSocket.CLOSED)) {
            // Подключаемся только если есть хотя бы одна целевая вкладка
            try {
                // Подключаемся только если есть хотя бы одна целевая вкладка
                ws = new WebSocket('ws://localhost:5000/');

                ws.onopen = function () {
                    console.log('WebSocket connection opened.');
                    setAllTabsDiscardable(true);
                    SetConnectionStatus('Connected');

                    // Установить интервал keep-alive сообщений
                    keepAliveInterval = setInterval(() => {
                        if (ws && ws.readyState === WebSocket.OPEN) {
                            ws.send(KEEP_ALIVE_MSG);
                        }
                    }, KEEP_ALIVE_INTERVAL);
                };

                // При получении сообщения
                ws.onmessage = function (event) {
                    console.log(`Received message: ${event.data}`);  // Логирование
                    chrome.storage.local.set({ lastReceivedMessage: event.data });

                    // Проверяем состояние опций "Auto-insert" и "Auto-tell"
                    chrome.storage.local.get(['autoInsert', 'autoTell'], function (result) {
                        if (result.autoInsert) {
                            targetTabs.forEach((tabId) => { // Отправляем сообщение на каждую вкладку в списке
                                chrome.tabs.sendMessage(tabId, { action: "insertMessage", data: event.data, autoTell: result.autoTell });
                            });
                        }
                    });
                };

                ws.onclose = function (event) {
                    setAllTabsDiscardable(false);
                    clearInterval(keepAliveInterval);  // Очистка интервала при закрытии соединения

                    if (event.wasClean) {
                        console.log('WebSocket connection closed cleanly.');
                        SetConnectionStatus('Disconnected');
                    } else {
                        console.log('WebSocket connection closed with error code:', event.code, 'Reason:', event.reason);
                        SetConnectionStatus('Disconnected');
                    }

                    ws = null;
                };

                ws.onerror = function () {
                    setAllTabsDiscardable(false);
                    console.log("WebSocket Error");
                    SetConnectionStatus('Disconnected');
                };
            }
            catch (error) {
                setAllTabsDiscardable(false);
                console.log('Failed to establish a WebSocket connection: ', error);
                SetConnectionStatus('Failed to connect');
            }
        }
        else
        {
            SetConnectionStatus('Searching for tab');
        }
    }

function attemptToConnectWebSocket() {
    if (targetTabs.size > 0 && (!ws || ws.readyState === WebSocket.CLOSED)) {
        reconnectAttempts = 0;
        connectWebSocketWithReconnect();
    }
}

function connectWebSocketWithReconnect() {
    if (targetTabs.size > 0 && reconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
        reconnectAttempts++;
        console.log(`Attempting to connect WebSocket (Attempt: ${reconnectAttempts})`);
        connectWebSocket();
        // Запланировать следующую попытку подключения
        if (!reconnectInterval) {
            reconnectInterval = setInterval(() => {
                if (ws && ws.readyState === WebSocket.OPEN) {
                    clearInterval(reconnectInterval);
                    reconnectInterval = null;
                } else if (reconnectAttempts >= MAX_RECONNECT_ATTEMPTS) {
                    clearInterval(reconnectInterval);
                    reconnectInterval = null;
                    console.log('Reached maximum reconnect attempts, stopping reconnection attempts');
                } else {

                    // Try reconnecting after 5 seconds
                    console.log('Attempting to reconnect in 5 seconds...');
                    SetConnectionStatus('Trying to reconnect');
                    connectWebSocketWithReconnect();
                }
            }, RECONNECT_ATTEMPT_INTERVAL);
        }
    } else {
        // Если достигнут максимум попыток и интервал все еще активен, его нужно остановить
        if (reconnectInterval) {
            clearInterval(reconnectInterval);
            reconnectInterval = null;
        }
        console.log('Reached maximum reconnect attempts, stopping reconnection attempts');
    }
}

function popupOpened() {
    // Обнуляем попытки переподключения при открытии pop-up
    reconnectAttempts = 0;
    attemptToConnectWebSocket()
}

// Слушатель сообщений для обработки действий pop-up
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    // Обработчики сообщений остаются без изменений
    if (message.action === "popupOpened") {
        popupOpened(); // Вызывается, когда открывается pop-up
    }
});



    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
        switch (message.action) {
            case "sendMessage":
                if (ws && ws.readyState === WebSocket.OPEN) {
                    console.log(`Sending message: ${message.data}`);
                    ws.send(message.data);
                    sendResponse({ status: 'sent' });
                } else {
                    console.log(`Error sending message or WebSocket not open. Message: ${message.data}`);
                    sendResponse({ status: 'error' });
                }
                break;
            case "toggleAutoSend":
                chrome.storage.local.set({ autoSend: message.autoSend });
                chrome.tabs.query({url: "https://chat.openai.com/*"}, (tabs) => {
                    tabs.forEach((tab) => {
                        // Проверяем, что вкладка активна и не закрыта перед отправкой сообщения
                        if (!tab.discarded && !tab.pendingUrl) {
                            chrome.tabs.sendMessage(tab.id, {
                                action: "toggleAutoSend",
                                autoSend: message.autoSend
                            });
                        }
                    });
                });
                break;
        }

        // Необходимо вернуть true, если асинхронный ответ будет послан позже
        // return true;
    });

    // Инициируем подключение WebSocket при запуске и проверяем уже открытые вкладки
    console.log('Checking for target tabs...');
    SetConnectionStatus('Searching for tab'); // Изначально устанавливаем статус "Searching for tab"

    // Проверяем уже открытые вкладки на наличие целевых
    chrome.tabs.query({ url: "https://chat.openai.com/*" }, function (tabs) {
        tabs.forEach(tab => {
            // Добавляем найденные целевые вкладки в список
            targetTabs.add(tab.id);
        });

        // Если нашли хотя бы одну целевую вкладку, пробуем подключиться
        if (targetTabs.size > 0) {
            attemptToConnectWebSocket()
        }
    });