    let ws = null;
    let keepAliveInterval = null;
    const KEEP_ALIVE_MSG = 'keep-alive';
    const KEEP_ALIVE_INTERVAL = 30000; // Отправлять keep-alive сообщение каждые 30 секунд

    const targetTabs = new Set(); // Используем Set для хранения уникальных идентификаторов вкладок

    function setTabNonDiscardable(tabId) {
        chrome.tabs.update(tabId, { autoDiscardable: false });
    }

    chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
        if (tab.url && tab.url.includes("https://chat.openai.com/")) {
            setTabNonDiscardable(tabId);
            targetTabs.add(tabId); // Добавляем вкладку в список
            attemptToConnectWebSocket(); // Пытаемся установить подключение при обнаружении целевой вкладки
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
            setTabNonDiscardable(tab.id);
        }
    });

    function attemptToConnectWebSocket() {
        if (!ws || ws.readyState === WebSocket.CLOSED) {
            connectWebSocket();
        }
    }

    function SetConnectionStatus(status) {
        console.log(`Setting connection status to: ${status}`);  // Добавлено логирование
        chrome.storage.local.set({ ConnectionStatus: status });
    }

    function connectWebSocket() {

        if (targetTabs.size > 0) {
            // Подключаемся только если есть хотя бы одна целевая вкладка
            try {
                // Подключаемся только если есть хотя бы одна целевая вкладка
                ws = new WebSocket('ws://localhost:5000/');

                ws.onopen = function () {
                    console.log('WebSocket connection opened.');
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
                                chrome.scripting.executeScript({
                                    target: { tabId: tabId },
                                    function: insertAndPossiblySend,
                                    args: [event.data, result.autoTell]
                                });
                            });
                        }
                    });
                };

                ws.onclose = function (event) {
                    clearInterval(keepAliveInterval);  // Очистка интервала при закрытии соединения

                    if (event.wasClean) {
                        console.log('WebSocket connection closed cleanly.');
                        SetConnectionStatus('Disconnected');
                    } else {
                        console.log('WebSocket connection closed with error code:', event.code, 'Reason:', event.reason);
                        SetConnectionStatus('Error');
                    }

                    ws = null;

                    // Try reconnecting after 5 seconds
                    console.log('Attempting to reconnect in 5 seconds...');
                    SetConnectionStatus('Trying to reconnect');
                    setTimeout(connectWebSocket, 10000);
                };

                ws.onerror = function () {
                    console.log("WebSocket Error");
                    SetConnectionStatus('Error');
                };
            }
            catch (error) {
                console.log('Failed to establish a WebSocket connection: ', error);
                SetConnectionStatus('Failed to connect');
                setTimeout(connectWebSocket, 10000);
            }
        }
        else
        {
            SetConnectionStatus('Searching for tab');
        }
    }

    function insertAndPossiblySend(message, autoTell) {
        const textarea = document.getElementById('prompt-textarea');
        const sendButton = document.querySelector('[data-testid="send-button"]');
        if (textarea) {
            // Сначала удаляем атрибут disabled у кнопки отправки, если он есть
            sendButton.removeAttribute('disabled');

            // Вставляем сообщение в textarea
            textarea.value = message;

            // Имитируем событие input для обновления состояния React или других фреймворков
            const event = new Event('input', { bubbles: true });
            textarea.dispatchEvent(event);

            // Проверяем, активирован ли autoTell и нажимаем кнопку, если она активна
            if (autoTell && sendButton && !sendButton.disabled) {
                sendButton.click();
            }
        }
    }

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
            // Добавляем найденные целевые вкладки в список и делаем их не выгружаемыми
            targetTabs.add(tab.id);
            setTabNonDiscardable(tab.id);
        });

        // Если нашли хотя бы одну целевую вкладку, пробуем подключиться
        if (targetTabs.size > 0) {
            attemptToConnectWebSocket();
        }
    });