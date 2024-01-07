// script.js begins

// Вызываем функции обновления для инициализации их текущими значениями из хранилища
chrome.storage.local.get(['ConnectionStatus', 'lastReceivedMessage'], (data) => {
    UpdateConnectionStatus(data.ConnectionStatus);
    updateLastReceivedMessage(data.lastReceivedMessage);
});

// Обновление статуса подключения при изменении в хранилище
chrome.storage.onChanged.addListener(function(changes, namespace) {
    for (let [key, { oldValue, newValue }] of Object.entries(changes)) {
        if (key === 'ConnectionStatus') {
            UpdateConnectionStatus(newValue);
        } else if (key === 'lastReceivedMessage') {
            updateLastReceivedMessage(newValue);
        }
    }
});

document.getElementById('sendMessageButton').addEventListener('click', () => {
    const messageToSend = document.getElementById('messageInput').value;
    chrome.runtime.sendMessage({ action: "sendMessage", data: messageToSend }, (response) => {
        if (response && response.status === 'sent') {
            console.log("Message sent to the server.");
        }
    });
    
});

// Обновляем статус подключения с новым значением
function UpdateConnectionStatus(newValue) {
    const statusDiv = document.getElementById('ConnectionStatus');
    statusDiv.textContent = newValue || 'Unknown';
}

// Обновляем последнее полученное сообщение с новым значением
function updateLastReceivedMessage(newValue) {
    const messageDiv = document.getElementById('lastReceivedMessage');
    messageDiv.textContent = newValue || 'None';
}

// При загрузке попапа устанавливаем состояния чекбоксов "Auto-insert" и "Auto-tell"
chrome.storage.local.get(['autoInsert', 'autoTell'], function (data) {
    if (data.autoInsert !== undefined) {
        document.getElementById('autoInsertCheckbox').checked = data.autoInsert;
    }
    if (data.autoTell !== undefined) {
        document.getElementById('autoTellCheckbox').checked = data.autoTell;
        // Установка доступности чекбокса "Auto-tell" в зависимости от состояния "Auto-insert"
        document.getElementById('autoTellCheckbox').disabled = !data.autoInsert;
    }
});

// При изменении состояния чекбокса "Auto-insert"
document.getElementById('autoInsertCheckbox').addEventListener('change', function() {
    const isChecked = this.checked;
    chrome.storage.local.set({ autoInsert: isChecked });

    // Если "Auto-insert" не активирован, отключаем "Auto-tell"
    const autoTellCheckbox = document.getElementById('autoTellCheckbox');
    autoTellCheckbox.disabled = !isChecked;
    autoTellCheckbox.checked = false;
    autoTellCheckbox.style.opacity = isChecked ? '1' : '0.5';
    chrome.storage.local.set({ autoTell: false });
});

// При изменении состояния чекбокса "Auto-tell"
document.getElementById('autoTellCheckbox').addEventListener('change', function() {
    const isChecked = this.checked;
    chrome.storage.local.set({ autoTell: isChecked });
});

// Слушаем изменения чекбокса "Auto-send" в попапе
document.getElementById('autoSendCheckbox').addEventListener('change', function() {
    // Отправляем состояние чекбокса в background script
    chrome.runtime.sendMessage({
        action: "toggleAutoSend",
        autoSend: this.checked
    });
});

// При загрузке попапа устанавливаем состояние чекбокса "Auto-send"
chrome.storage.local.get('autoSend', function(data) {
    document.getElementById('autoSendCheckbox').checked = data.autoSend || false;
});
