//content.js begins
let generationStatus = "finished"; // Изначально устанавливаем статус как "finished"

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

function sendTextMessage(text) {
    chrome.runtime.sendMessage({ action: "sendMessage", data: text });
    console.log("sending : " + text);
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
                sendTextMessage(codeContent);
            });
            copyButton.parentNode.parentNode.insertBefore(sendButton, copyButton.nextSibling);
        }
    }
}

var isAutoSendOn = false;


// Слушаем сообщения от background script
chrome.runtime.onMessage.addListener(function(message, sender, sendResponse) {
    if (message.action === "toggleAutoSend") {
        isAutoSendOn = message.autoSend;
        console.log('isAutoSendOn is ' + isAutoSendOn)
        if (!message.autoSend) disableAutoSendForAllCodeBlocks();
    }
    if (message.action === "insertMessage") {
        insertAndPossiblySend(message.data, message.autoTell);
    }
});


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
            if (counter === 0 && !isCancelled) {
                clearInterval(intervalId);
                sendTextMessage(codeBlock.textContent.trim());
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

const observer = new MutationObserver((mutationsList) => {
    for (const mutation of mutationsList) {
        if (mutation.type === 'childList') {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    // Обработка новых сообщений
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

                    // Обработка изменений в контейнере с ролью presentation или с нужным классом
                    if ((node.tagName === 'DIV' && node.getAttribute('role') === 'presentation') ||
                        (node.tagName === 'DIV' && Array.from(node.classList).some(className => className.startsWith('react-scroll-to-bottom')))) {
                        console.log('Обработка старых сообщений');
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

            // Имитируем событие input для обновления состояния React или других фреймворков
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
