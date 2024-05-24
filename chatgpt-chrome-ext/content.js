//content.js begins

let ws = null;
let keepAliveInterval = null;
let reconnectInterval = null;
let reconnectAttempts = 0;
const KEEP_ALIVE_MSG = 'keep-alive';
const KEEP_ALIVE_INTERVAL = 30000; // Отправлять keep-alive сообщение каждые 30 секунд
const RECONNECT_ATTEMPT_INTERVAL = 5000; // Попытка переподключения каждые 5 секунд
const MAX_RECONNECT_ATTEMPTS = 12; // Максимум 12 попыток в течение минуты (60 / 5)
let generationStatus = "finished"; // Изначально устанавливаем статус как "finished"
var isAutoSendOn = false;

try {
    console.log("Loading SignalR...");
    const signalR = window.signalR;

    if (!signalR) {
        throw new Error('SignalR library failed to load.');
    }

    // Создайте соединение
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("http://localhost:5005/chathub", { withCredentials: false })
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // Определите методы, которые могут быть вызваны сервером
    connection.on("ClientMethod", (message) => {
        console.log(`Client method called with message: ${message}`);
        // Реализуйте логику для обработки вызова метода
        // Здесь не нужно ничего возвращать
    });

    // Начните соединение
    connection.start().then(async () => {
        console.log("SignalR connected");

        // Пример вызова метода RPC на сервере
        const echoResponse = await connection.invoke("Echo", "Test message");
        console.log(`Echo response: ${echoResponse}`);

        // Пример вызова метода RPC на клиенте с сервера
        await connection.invoke("CallClientMethod", "Message to client");
        console.log('Client method invoked from server.');

        // Функция для отправки команд на сервер
        function SendCommand(type, content) {
            connection.invoke("SendMessage", "ChromeExtension", JSON.stringify({ type, content }))
                .catch(err => console.error(err.toString()));
        }

        // Пример использования SendCommand
        SendCommand("commandType", "commandContent");

    }).catch(err => {
        console.error("Error connecting to SignalR", err);
    });

} catch (error) {
    console.error('Error during SignalR setup:', error);
}





function SetConnectionStatus(status) {
    console.log(`Setting connection status to: ${status}`);
    localStorage.setItem('ConnectionStatus', status);
    document.getElementById('ConnectionStatus').textContent = status;
}

function connectWebSocket() {
    if (!ws || ws.readyState === WebSocket.CLOSED) {
        try {
            ws = new WebSocket('ws://localhost:5001/');

            ws.onopen = function () {
                console.log('WebSocket connection opened.');
                SetConnectionStatus('Connected');

                // Setup keep-alive message sending
                keepAliveInterval = setInterval(() => {
                    if (ws && ws.readyState === WebSocket.OPEN) {
                        const keepAliveMessage = JSON.stringify({ type: "keepAlive", content: "ping" });
                        ws.send(keepAliveMessage);
                    }
                }, KEEP_ALIVE_INTERVAL);
            };

            ws.onmessage = function (event) {
                console.log(`Received message: ${event.data}`);
                try {
                    const messageData = JSON.parse(event.data);
                    const autoInsert = localStorage.getItem('autoInsert') === 'true';  // Outside the switch block
                    const autoTell = localStorage.getItem('autoTell') === 'true';

                    switch (messageData.Type) {
                        case "text":
                            updateLastReceivedMessage(messageData.Content);
                            localStorage.setItem('lastReceivedMessage', messageData.Content);
                            console.log(`Auto Insert: ${autoInsert}, Auto Tell: ${autoTell}`);
                            if (autoInsert) {
                                insertAndPossiblySend(messageData.Content, autoTell);
                            }
                            break;
                        default:
                            console.error("Received unknown message type:", messageData.Type);
                    }
                } catch (error) {
                    console.error("Error parsing JSON from WebSocket message:", error);
                }
            };

            ws.onclose = function (event) {
                clearInterval(keepAliveInterval);  // Clear the interval on close
                console.log(`WebSocket connection closed ${event.wasClean ? 'cleanly' : 'with error'}: Code=${event.code}, Reason=${event.reason}`);
                SetConnectionStatus('Disconnected');
                ws = null;
                // Consider re-connecting automatically here
            };

            ws.onerror = function (error) {
                console.log("WebSocket Error:", error);
                SetConnectionStatus('Disconnected');
            };
        }
        catch (error) {
            console.log('Failed to establish a WebSocket connection:', error);
            SetConnectionStatus('Failed to connect');
        }
    }
}


function connectWebSocketWithReconnect() {
    if (reconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
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

function SendCommand(type, content) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        const message = JSON.stringify({ type: type, content: content });
        console.log(`Sending message: ${message}`);
        ws.send(message);
    } else {
        console.log(`WebSocket not open. Failed to send message: ${content}`);
    }
}

// Обновляем статус подключения с новым значением
function UpdateConnectionStatus(newValue) {
    const statusDiv = document.getElementById('ConnectionStatus');
    statusDiv.textContent = newValue || 'Unknown';
    localStorage.setItem('connectionStatus', statusDiv.textContent);
}

// Обновляем последнее полученное сообщение с новым значением
function updateLastReceivedMessage(newValue) {
    const messageDiv = document.getElementById('lastReceivedMessage');
    messageDiv.textContent = newValue || 'None';
    localStorage.setItem('lastReceivedMessage', messageDiv.textContent);
}

function updateGenerationStatus() {
    const promptContainer = document.querySelector('form');
    const sendButton = document.querySelector('[data-testid="send-button"]');
    const stopButton = document.querySelector('button[aria-label="Stop generating"]');

    let result;

    if (!promptContainer) {
        result = "error";
    } else if (stopButton) {
        result = "progress";
    } else if (sendButton) {
        result = "finished";
    }

    if(result !== generationStatus){
        generationStatus = result;
        console.log(`Current generation status: ${generationStatus}`);
        document.body.dispatchEvent(new CustomEvent("generationStatusChanged", { detail: { status: generationStatus } }));
    }
}

function appendSendButton(container) {
    // Проверяем, содержит ли контейнер уже кнопку "Send"
    if (!container.querySelector(".send-button")) {
        const copyButton = container.querySelector('button');
        // Проверяем, является ли это кнопкой "Copy code"
        if (copyButton && copyButton.textContent.includes("Copy code")) {
            const sendButton = document.createElement("button");
            sendButton.textContent = "Send";
            sendButton.className = "send-button"; // Добавляем уникальный класс для кнопки "Send"
            sendButton.style = "margin-left: 10px;padding-left: 10px;";
            // Слушатель для отправки содержимого кода на момент клика
            sendButton.addEventListener('click', () => {
                // Получаем актуальный текст на момент клика
                const codeContent = container.querySelector('code').textContent;
                SendCommand('command', codeContent);
            });
            copyButton.parentNode.parentNode.insertBefore(sendButton, copyButton.nextSibling);
        }
    }
}

// Функция для добавления таймера к блоку кода
function addTimerToCodeBlock(codeBlock) {
    const controlPanel = codeBlock.closest('.rounded-md').querySelector('.flex.items-center');
    const sendButton = controlPanel.querySelector('.send-button');

    // Удаляем кнопку "Send", если она существует
    if (sendButton) {
        sendButton.remove();
    }

    // Создаем элемент таймера
    const timerSpan = document.createElement("span");
    timerSpan.className = "auto-send-timer";
    timerSpan.textContent = "Waiting for generation to end";
    timerSpan.style = "margin-left: 10px;";

    // Создаем кнопку "Cancel"
    const cancelButton = document.createElement("button");
    cancelButton.textContent = "Cancel";
    cancelButton.className = "cancel-button";
    cancelButton.style = "margin-left: 5px;";

    // Добавляем таймер и кнопку "Cancel" в контейнер кнопок
    controlPanel.appendChild(timerSpan);
    controlPanel.appendChild(cancelButton);

    let counter = 5;
    let intervalId;
    let isCancelled = false;

    // Обработчик нажатия кнопки "Cancel"
    cancelButton.addEventListener('click', () => {
        clearInterval(intervalId);
        isCancelled = true;
        timerSpan.remove();
        cancelButton.remove();
        // Возвращаем кнопку "Send"
        controlPanel.appendChild(sendButton);
    });
    const startTimer = () => {

        // Начинаем обратный отсчет
         intervalId = setInterval(() => {
            counter--;
             if (ws && ws.readyState === WebSocket.OPEN && counter === 0 && !isCancelled) {
                clearInterval(intervalId);
                 SendCommand('command', codeBlock.textContent.trim());
                timerSpan.textContent = 'Sent';
                // Удаляем таймер и кнопку "Cancel" из DOM после небольшой задержки
                setTimeout(() => {
                    timerSpan.remove();
                    cancelButton.remove();
                    // Возвращаем кнопку "Send"
                    controlPanel.appendChild(sendButton);
                }, 2000);
            } else {
                timerSpan.textContent = `Sending in ${counter}...`;
            }
        }, 1000);

        timerSpan.intervalId = intervalId;


    };

    // Начало отсчета, если генерация уже завершена
    if (generationStatus === "finished") {
        startTimer();
    } else {
        // Функция обработки события изменения статуса
        const handleStatusChange = (event) => {
            if (event.detail.status === "finished") {
                startTimer();
                document.body.removeEventListener("generationStatusChanged", handleStatusChange);
            }
        };

        // Добавление обработчика событий
        document.body.addEventListener("generationStatusChanged", handleStatusChange);
    }


}

// Функция для удаления таймеров и кнопок "Cancel" со всех блоков кода
function disableAutoSendForAllCodeBlocks() {
    const timers = document.querySelectorAll('.auto-send-timer');
    timers.forEach(timer => {
        // Отменяем таймер, используя сохраненный intervalId
        clearInterval(timer.intervalId);
        timer.remove();
    });
    // Удаляем все кнопки "Cancel"
    document.querySelectorAll('.cancel-button').forEach(button => button.remove());
}
function createSettingsButton() {
    const existingButtonContainer = document.querySelector('.sticky .flex.gap-2.pr-1');
    if (!existingButtonContainer) return;

    // Check if our button already exists
    if (document.querySelector('#my-settings-button')) return;

    // Create the new button
    const settingsButton = document.createElement('button');
    settingsButton.id = 'my-settings-button';
    settingsButton.className = 'btn relative btn-secondary btn-small flex h-9 w-9 items-center justify-center whitespace-nowrap rounded-lg';
    settingsButton.innerHTML = `<div class="flex w-full gap-2 items-center justify-center">
         <svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg" class="icon-md"><path d="M11.6439 3C10.9352 3 10.2794 3.37508 9.92002 3.98596L9.49644 4.70605C8.96184 5.61487 7.98938 6.17632 6.93501 6.18489L6.09967 6.19168C5.39096 6.19744 4.73823 6.57783 4.38386 7.19161L4.02776 7.80841C3.67339 8.42219 3.67032 9.17767 4.01969 9.7943L4.43151 10.5212C4.95127 11.4386 4.95127 12.5615 4.43151 13.4788L4.01969 14.2057C3.67032 14.8224 3.67339 15.5778 4.02776 16.1916L4.38386 16.8084C4.73823 17.4222 5.39096 17.8026 6.09966 17.8083L6.93502 17.8151C7.98939 17.8237 8.96185 18.3851 9.49645 19.294L9.92002 20.014C10.2794 20.6249 10.9352 21 11.6439 21H12.3561C13.0648 21 13.7206 20.6249 14.08 20.014L14.5035 19.294C15.0381 18.3851 16.0106 17.8237 17.065 17.8151L17.9004 17.8083C18.6091 17.8026 19.2618 17.4222 19.6162 16.8084L19.9723 16.1916C20.3267 15.5778 20.3298 14.8224 19.9804 14.2057L19.5686 13.4788C19.0488 12.5615 19.0488 11.4386 19.5686 10.5212L19.9804 9.7943C20.3298 9.17767 20.3267 8.42219 19.9723 7.80841L19.6162 7.19161C19.2618 6.57783 18.6091 6.19744 17.9004 6.19168L17.065 6.18489C16.0106 6.17632 15.0382 5.61487 14.5036 4.70605L14.08 3.98596C13.7206 3.37508 13.0648 3 12.3561 3H11.6439Z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"></path><circle cx="12" cy="12" r="2.5" stroke="currentColor" stroke-width="2"></circle></svg>
    </div>`;

    // Add click event listener to open settings popup
    settingsButton.addEventListener('click', () => {
        toggleSettingsPopup(); // Function to handle opening/closing of the settings popup
    });

    // Append the button to the existing container
    existingButtonContainer.appendChild(settingsButton);
}

// Function to show or hide the settings popup
function createSettingsPopup() {
    const existingContainer = document.querySelector('.sticky .flex.gap-2.pr-1');
    if (!existingContainer) return;

    // Check if our popup already exists
    if (document.querySelector('#my-settings-popup')) return;

    // Create the popup HTML content with modified styles
    const popupHTML = `
        <div id="my-settings-popup" style="display: none; position: fixed;top: 20%;left: 53%;transform: translate(-50%, -10%);z-index: 1000;background-color: #212121; padding: 20px;border-radius: 8px; border-width: 3px; color: white;">
            <h3>WebSocket Status:</h3>
            <div id="ConnectionStatus">Checking...</div>

            <h3>Last Received Message:</h3>
            <div id="lastReceivedMessage">None</div>

            <h3>Options:</h3>
            <label><input type="checkbox" id="autoInsertCheckbox"> Auto-insert</label><br>
            <label><input type="checkbox" id="autoTellCheckbox" disabled> Auto-tell</label><br>
            <label><input type="checkbox" id="autoSendCheckbox"> Auto-send</label>

            <h3>Send Message:</h3>
            <input type="text" id="messageInput" placeholder="Type a message...">
            <button id="sendMessageButton" style="margin-left: 10px;">Send</button>
        </div>
    `;

    // Insert the popup HTML into the body element
    document.body.insertAdjacentHTML('beforeend', popupHTML);


    // Вызываем функции обновления для инициализации их текущими значениями из хранилища
    localStorage.getItem(['ConnectionStatus', 'lastReceivedMessage'], (data) => {
        UpdateConnectionStatus(data.ConnectionStatus);
        updateLastReceivedMessage(data.lastReceivedMessage);
    });

    // При загрузке попапа устанавливаем состояния чекбоксов "Auto-insert", "Auto-tell" и "Auto-Send"
    localStorage.getItem(['autoInsert', 'autoTell', 'autoSend'], function (data) {
        if (data.autoInsert !== undefined) {
            document.getElementById('autoInsertCheckbox').checked = data.autoInsert;
        }
        if (data.autoTell !== undefined) {
            document.getElementById('autoTellCheckbox').checked = data.autoTell;
            // Установка доступности чекбокса "Auto-tell" в зависимости от состояния "Auto-insert"
            document.getElementById('autoTellCheckbox').disabled = !data.autoInsert;
        }
        if (data.autoSend !== undefined) {
            document.getElementById('autoSendCheckbox').checked = data.autoSend;
        }
    });

    // При изменении состояния чекбокса "Auto-insert"
    document.getElementById('autoInsertCheckbox').addEventListener('change', function () {
        const isChecked = this.checked;
        localStorage.setItem('autoInsert', isChecked );

        // Если "Auto-insert" не активирован, отключаем "Auto-tell"
        const autoTellCheckbox = document.getElementById('autoTellCheckbox');
        autoTellCheckbox.disabled = !isChecked;
        autoTellCheckbox.checked = false;
        autoTellCheckbox.style.opacity = isChecked ? '1' : '0.5';
        localStorage.setItem('autoTell', false);
        console.log('autoInsert set to ' + isChecked);
    });

    // При изменении состояния чекбокса "Auto-tell"
    document.getElementById('autoTellCheckbox').addEventListener('change', function () {
        const isChecked = this.checked;
        localStorage.setItem('autoTell', isChecked);
        console.log('autoTell set to ' + isChecked);
    });

    // Слушаем изменения чекбокса "Auto-send" в попапе
    document.getElementById('autoSendCheckbox').addEventListener('change', function () {
        isAutoSendOn = this.checked;
        console.log('isAutoSendOn is ' + isAutoSendOn)
        if (!isAutoSendOn) disableAutoSendForAllCodeBlocks();
        localStorage.setItem('autoSend', isAutoSendOn);
        console.log('autoSend set to ' + isAutoSendOn);
    });

    document.getElementById('sendMessageButton').addEventListener('click', () => {
        const messageToSend = document.getElementById('messageInput').value;
        SendCommand('command', messageToSend);
    });
}

function toggleSettingsPopup() {
    const popup = document.getElementById('my-settings-popup');
    if (popup.style.display === 'none') {
        reconnectAttempts = 0;
        connectWebSocketWithReconnect();
    }
    popup.style.display = popup.style.display === 'block' ? 'none' : 'block';
}

const observer = new MutationObserver((mutationsList) => {
    for (const mutation of mutationsList) {
        if (mutation.type === 'childList') {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    // Обработка новых сообщений в текущем открытом диалоге
                    if (node.tagName === 'PRE') {
                        const codeBlock = node.querySelector('code.hljs');
                        if (codeBlock) {
                            console.log('Обработка нового сообщения');
                            appendSendButton(node); // Вызываем для блока <pre>
                            if (isAutoSendOn) {
                                addTimerToCodeBlock(codeBlock);
                            }
                        }
                    }

                    // Обработка изменений в контейнере с ролью presentation или с нужным классом (при начале нового диалога или переключении на уже существующий)
                    if ((node.tagName === 'DIV' && node.getAttribute('role') === 'presentation') ||
                        (node.tagName === 'DIV' && Array.from(node.classList).some(className => className.startsWith('react-scroll-to-bottom')))) {
                        console.log('Обработка старых сообщений');
                        createSettingsButton();
                        createSettingsPopup();
                        const preNodes = node.querySelectorAll('pre');
                        preNodes.forEach((preNode) => {
                            appendSendButton(preNode); // Вызываем для блока <pre>
                        });
                    }

                }
            });

        }
    }
    updateGenerationStatus();
});


function insertAndPossiblySend(message, autoTell) {
    const performInsertAndSend = () => {
        const textarea = document.getElementById('prompt-textarea');
        const sendButton = document.querySelector('[data-testid="send-button"]');
        if (textarea) {
            // Сначала удаляем атрибут disabled у кнопки отправки, если он есть
            sendButton.removeAttribute('disabled');

            // Вставляем сообщение в textarea
            textarea.value = message;

            // Имитируем событие input для обновления состояния 
            const event = new Event('input', { bubbles: true });
            textarea.dispatchEvent(event);

            // Проверяем, активирован ли autoTell и нажимаем кнопку, если она активна
            if (autoTell && sendButton && !sendButton.disabled) {
                sendButton.click();
            }
        }
    };

    if (generationStatus === "finished") {
        performInsertAndSend();
    } else {
        const handleStatusChange = (event) => {
            if (event.detail.status === "finished") {
                performInsertAndSend();
                document.body.removeEventListener("generationStatusChanged", handleStatusChange);
            }
        };

        document.body.addEventListener("generationStatusChanged", handleStatusChange);
    }
}

// Настройка и запуск наблюдения
observer.observe(document.body, { childList: true, subtree: true });

console.log('script loaded');
