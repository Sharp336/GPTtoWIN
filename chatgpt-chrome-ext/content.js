// Constants
const KEEP_ALIVE_MSG = 'keep-alive';
const KEEP_ALIVE_INTERVAL = 30000; // 30 seconds
const RECONNECT_ATTEMPT_INTERVAL = 5000; // 5 seconds
const MAX_RECONNECT_ATTEMPTS = 12; // 1 minute
let connection = null;
let keepAliveInterval = null;
let reconnectAttempts = 0;
let isAutoSendOn = false;


// Load SignalR and initialize connection
(async () => {
    try {
        console.log("Loading SignalR...");
        const signalR = window.signalR;
        if (!signalR) {
            throw new Error('SignalR library failed to load.');
        } else {
            console.log('SignalR library loaded.')
            initializeSignalR(signalR);
        }

        await import(chrome.runtime.getURL('lib/chatgpt.js'));

    } catch (error) {
        console.error('Error during startup:', error);
    }
})();



// Initialize SignalR connection
function initializeSignalR(signalR) {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("http://localhost:5005/chathub", { withCredentials: false })
        .configureLogging(signalR.LogLevel.Information)
        .build();

    connection.on("ClientMethod", (message) => {
        console.log(`Client method called with message: ${message}`);
        // Handle client method call from server
    });

    connection.on("ReceiveMessage", (user, message) => {
        console.log(`Received message: ${message}`);
        handleReceivedMessage(message);
    });

    connection.on("getChatData", async () => {
        console.log('getChatData called from server')
        let data = await chatgpt.getChatData();
        console.log('getChatData returns data:\n' + data )
        return JSON.stringify(data);
    });


    connection.onclose(async () => {
        console.log("SignalR connection closed");
        SetConnectionStatus('Disconnected');
        startReconnectInterval();
    });

    startConnection();
}

// Start SignalR connection
function startConnection() {
    connection.start().then(async () => {
        console.log("SignalR connected");
        SetConnectionStatus('Connected');
        setupKeepAlive();
        resetReconnectAttempts();
    }).catch(err => {
        console.log(`Expected error during SignalR connection attempt: ${err.message}`);
        SetConnectionStatus('Disconnected');
        startReconnectInterval();
    });
}

// Start reconnection attempts
function startReconnectInterval() {
    if (reconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
        reconnectAttempts++;
        console.log(`Attempting to reconnect SignalR (Attempt: ${reconnectAttempts})`);
        setTimeout(() => {
            try {
                startConnection();
            } catch (err) {
                console.log(`Expected error during SignalR reconnection attempt: ${err.message}`);
                startReconnectInterval();
            }
        }, RECONNECT_ATTEMPT_INTERVAL);
    } else {
        console.log('Reached maximum reconnect attempts, stopping reconnection attempts');
    }
}



// Setup keep-alive messages
function setupKeepAlive() {
    keepAliveInterval = setInterval(() => {
        if (connection && connection.state === signalR.HubConnectionState.Connected) {
            connection.invoke("KeepAlive", KEEP_ALIVE_MSG).catch(err => console.error(err.toString()));
        }
    }, KEEP_ALIVE_INTERVAL);
}


// Reset reconnect attempts counter
function resetReconnectAttempts() {
    reconnectAttempts = 0;
}

// Handle received messages
function handleReceivedMessage(message) {
    const autoInsert = localStorage.getItem('autoInsert') === 'true';
    const autoTell = localStorage.getItem('autoTell') === 'true';

    updateLastReceivedMessage(message);
    localStorage.setItem('lastReceivedMessage', message);
    if (autoInsert) { insertAndPossiblySend(message, autoTell); }
}

// Set connection status
function SetConnectionStatus(status) {
    console.log(`Setting connection status to: ${status}`);
    localStorage.setItem('ConnectionStatus', status);
    const statusElement = document.getElementById('ConnectionStatus');
    if (statusElement) {
        statusElement.textContent = status;
    }
}

// Send command to server
function SendCommand(content) {
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
        console.log(`Sending message: ${content}`);
        connection.invoke("SendMessage", "ChromeExtension", content)
            .catch(err => console.error(err.toString()));
    } else {
        console.log(`SignalR not connected. Failed to send message: ${content}`);
    }
}


// Update connection status in DOM
function UpdateConnectionStatus(newValue) {
    const statusDiv = document.getElementById('ConnectionStatus');
    if (statusDiv) {
        statusDiv.textContent = newValue || 'Unknown';
        localStorage.setItem('connectionStatus', statusDiv.textContent);
    }
}

// Update last received message in DOM
function updateLastReceivedMessage(newValue) {
    const messageDiv = document.getElementById('lastReceivedMessage');
    if (messageDiv) {
        messageDiv.textContent = newValue || 'None';
        localStorage.setItem('lastReceivedMessage', messageDiv.textContent);
    }
}

// Insert and possibly send message
async function insertAndPossiblySend(message, autoTell) {
    console.log('insertAndPossiblySend called, autoTell = ' + autoTell + '\nmessage: ' + message);

    const performInsertAndSend = async () => {
        console.log('performInsertAndSend called');
        const chatBox = chatgpt.getChatBox();
        const sendButton = chatgpt.getSendButton();

        if (chatBox && sendButton) {
            sendButton.removeAttribute('disabled');
            chatBox.value = message;
            chatBox.dispatchEvent(new Event('input', { bubbles: true }));
            if (autoTell && !sendButton.disabled) {
                sendButton.click();
            }
        }
    };

    const isIdle = await chatgpt.isIdle();
    console.log('chatgpt.isIdle() = ' + isIdle);
    if (isIdle) {
        performInsertAndSend();
    } else {
        const handleStatusChange = async (event) => {
            if (await chatgpt.isIdle()) {
                performInsertAndSend();
                document.body.removeEventListener("generationStatusChanged", handleStatusChange);
            }
        };
        document.body.addEventListener("generationStatusChanged", handleStatusChange);
    }
}

// Add send button to code blocks
function appendSendButton(container) {
    const languageElement = container.querySelector('span:first-child');
    const allowedLanguages = JSON.parse(localStorage.getItem('allowedLanguages') || '[]');
    const language = languageElement ? languageElement.textContent.trim().toLowerCase() : '';

    if (!allowedLanguages.includes(language)) {
        return;
    }

    const copyButton = container.querySelector('button');
    if (copyButton) {
        const sendButton = document.createElement("button");
        sendButton.className = "send-button";
        sendButton.style = "margin-left: 10px; padding: 0 10px; display: inline-flex; align-items: center;";

        const iconSVG = `
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" fill="none" viewBox="0 0 24 24" class="icon-sm-heavy" style="margin-right: 5px;">
                <path fill="currentColor" fill-rule="evenodd" d="M11.293 3.293a1 1 0 0 1 1.414 0l4 4a1 1 0 0 1-1.414 1.414L13 6.414V15a1 1 0 1 1-2 0V6.414L8.707 8.707a1 1 0 0 1-1.414-1.414zM4 14a1 1 0 0 1 1 1v3a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-3a1 1 0 1 1 2 0v3a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3v-3a1 1 0 0 1 1-1" clip-rule="evenodd"></path>
            </svg>
        `;
        sendButton.innerHTML = `${iconSVG}<div>Send</div>`;

        sendButton.addEventListener('click', () => {
            const codeContent = container.querySelector('code').textContent;
            SendCommand( codeContent);
        });

        // Append the send button next to the copy button
        copyButton.parentNode.parentNode.insertBefore(sendButton, copyButton.nextSibling);
    }
}





// Add send button to all existing code blocks on page load
function addSendButtonToAllCodeBlocks() {
    const codeBlocks = document.querySelectorAll('pre');
    codeBlocks.forEach((codeBlock) => {
        appendSendButton(codeBlock);
    });
}

// Add timer to code blocks
function addTimerToCodeBlock(codeBlock) {
    const controlPanel = codeBlock.closest('.rounded-md').querySelector('.flex.items-center');
    let sendButton = controlPanel.querySelector('.send-button');

    if (sendButton) {
        sendButton.remove();
    }

    const timerSpan = document.createElement("span");
    timerSpan.className = "auto-send-timer";
    timerSpan.textContent = "Waiting for generation to end";
    timerSpan.style = "margin-left: 10px;";

    const cancelButton = document.createElement("button");
    cancelButton.textContent = "Cancel";
    cancelButton.className = "cancel-button";
    cancelButton.style = "margin-left: 5px;";

    controlPanel.appendChild(timerSpan);
    controlPanel.appendChild(cancelButton);

    let counter = 5;
    let intervalId;
    let isCancelled = false;

    cancelButton.addEventListener('click', () => {
        clearInterval(intervalId);
        isCancelled = true;
        timerSpan.remove();
        cancelButton.remove();
        if (!sendButton || !(sendButton instanceof Node)) {
            sendButton = document.createElement("button");
            sendButton.textContent = "Send";
            sendButton.className = "send-button";
            sendButton.style = "margin-left: 10px;padding-left: 10px;";
        }
        controlPanel.appendChild(sendButton);
    });

    const startTimer = () => {
        intervalId = setInterval(() => {
            counter--;
            if (connection && connection.state === signalR.HubConnectionState.Connected && counter === 0 && !isCancelled) {
                clearInterval(intervalId);
                SendCommand( codeBlock.textContent.trim());
                timerSpan.textContent = 'Sent';
                setTimeout(() => {
                    timerSpan.remove();
                    cancelButton.remove();
                    if (!sendButton || !(sendButton instanceof Node)) {
                        sendButton = document.createElement("button");
                        sendButton.textContent = "Send";
                        sendButton.className = "send-button";
                        sendButton.style = "margin-left: 10px;padding-left: 10px;";
                    }
                    controlPanel.appendChild(sendButton);
                }, 2000);
            } else {
                timerSpan.textContent = `Sending in ${counter}...`;
            }
        }, 1000);

        timerSpan.intervalId = intervalId;
    };

    chatgpt.isIdle().then((isIdle) => {
        if (isIdle) {
            startTimer();
        } else {
            const handleStatusChange = async (event) => {
                if (await chatgpt.isIdle()) {
                    startTimer();
                    document.body.removeEventListener("generationStatusChanged", handleStatusChange);
                }
            };
            document.body.addEventListener("generationStatusChanged", handleStatusChange);
        }
    });
}

// Disable auto-send for all code blocks
function disableAutoSendForAllCodeBlocks() {
    const timers = document.querySelectorAll('.auto-send-timer');
    timers.forEach(timer => {
        clearInterval(timer.intervalId);
        timer.remove();
    });
    document.querySelectorAll('.cancel-button').forEach(button => button.remove());
}

// Create settings button
function createSettingsButton() {
    const existingButtonContainer = document.querySelector('.sticky .flex.gap-2.pr-1');
    if (!existingButtonContainer) return;

    if (document.querySelector('#my-settings-button')) return;

    const settingsButton = document.createElement('button');
    settingsButton.id = 'my-settings-button';
    settingsButton.className = 'btn relative btn-secondary btn-small flex h-9 w-9 items-center justify-center whitespace-nowrap rounded-lg';
    settingsButton.innerHTML = `<div class="flex w-full gap-2 items-center justify-center">
         <svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg" class="icon-md"><path d="M11.6439 3C10.9352 3 10.2794 3.37508 9.92002 3.98596L9.49644 4.70605C8.96184 5.61487 7.98938 6.17632 6.93501 6.18489L6.09967 6.19168C5.39096 6.19744 4.73823 6.57783 4.38386 7.19161L4.02776 7.80841C3.67339 8.42219 3.67032 9.17767 4.01969 9.7943L4.43151 10.5212C4.95127 11.4386 4.95127 12.5615 4.43151 13.4788L4.01969 14.2057C3.67032 14.8224 3.67339 15.5778 4.02776 16.1916L4.38386 16.8084C4.73823 17.4222 5.39096 17.8026 6.09966 17.8083L6.93502 17.8151C7.98939 17.8237 8.96185 18.3851 9.49645 19.294L9.92002 20.014C10.2794 20.6249 10.9352 21 11.6439 21H12.3561C13.0648 21 13.7206 20.6249 14.08 20.014L14.5035 19.294C15.0381 18.3851 16.0106 17.8237 17.065 17.8151L17.9004 17.8083C18.6091 17.8026 19.2618 17.4222 19.6162 16.8084L19.9723 16.1916C20.3267 15.5778 20.3298 14.8224 19.9804 14.2057L19.5686 13.4788C19.0488 12.5615 19.0488 11.4386 19.5686 10.5212L19.9804 9.7943C20.3298 9.17767 20.3267 8.42219 19.9723 7.80841L19.6162 7.19161C19.2618 6.57783 18.6091 6.19744 17.9004 6.19168L17.065 6.18489C16.0106 6.17632 15.0382 5.61487 14.5036 4.70605L14.08 3.98596C13.7206 3.37508 13.0648 3 12.3561 3H11.6439Z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"></path><circle cx="12" cy="12" r="2.5" stroke="currentColor" stroke-width="2"></circle></svg>
    </div>`;

    settingsButton.addEventListener('click', () => {
        toggleSettingsPopup();
    });

    existingButtonContainer.appendChild(settingsButton);
}

function createSettingsPopup() {
    const existingContainer = document.querySelector('.sticky .flex.gap-2.pr-1');
    if (!existingContainer) return;

    if (document.querySelector('#my-settings-popup')) return;

    const popupHTML = `
    <div id="my-settings-popup" style="display: none; position: fixed;top: 20%;left: 53%;transform: translate(-50%, -10%);z-index: 1000;background-color: #2f2f2f; padding: 20px;border-radius: 8px; border-width: 3px; border-color: #171717; color: white;">
        <h3>Connection Status:</h3>
        <div id="ConnectionStatus">Checking...</div>

        <h3>Last Received Message:</h3>
        <div id="lastReceivedMessage">None</div>

        <h3>Options:</h3>
        <label><input type="checkbox" id="autoInsertCheckbox"> Auto-insert</label><br>
        <label><input type="checkbox" id="autoTellCheckbox" disabled> Auto-tell</label><br>
        <label><input type="checkbox" id="autoSendCheckbox"> Auto-send</label>

        <h3>Send Message:</h3>
        <input type="text" id="messageInput" placeholder="Type a message..." style="color: black;">
        <button id="sendMessageButton" style="margin-left: 10px;">Send</button>

        <h3>Allowed Languages:</h3>
        <input type="text" id="allowedLanguagesInput" placeholder="e.g., javascript, python" style="color: black;">
        <button id="saveLanguagesButton" style="margin-left: 10px;">Save</button>
    </div>
`;


    document.body.insertAdjacentHTML('beforeend', popupHTML);

    UpdateConnectionStatus(localStorage.getItem('ConnectionStatus') || 'Checking...');
    updateLastReceivedMessage(localStorage.getItem('lastReceivedMessage') || 'None');

    document.getElementById('autoInsertCheckbox').checked = localStorage.getItem('autoInsert') === 'true';
    document.getElementById('autoTellCheckbox').checked = localStorage.getItem('autoTell') === 'true';
    document.getElementById('autoSendCheckbox').checked = localStorage.getItem('autoSend') === 'true';

    document.getElementById('allowedLanguagesInput').value = JSON.parse(localStorage.getItem('allowedLanguages') || '[]').join(', ');

    document.getElementById('autoTellCheckbox').disabled = !document.getElementById('autoInsertCheckbox').checked;

    document.getElementById('autoInsertCheckbox').addEventListener('change', function () {
        const isChecked = this.checked;
        localStorage.setItem('autoInsert', isChecked);
        document.getElementById('autoTellCheckbox').disabled = !isChecked;
        document.getElementById('autoTellCheckbox').checked = false;
        localStorage.setItem('autoTell', false);
        console.log('autoInsert set to ' + isChecked);
    });

    document.getElementById('autoTellCheckbox').addEventListener('change', function () {
        const isChecked = this.checked;
        localStorage.setItem('autoTell', isChecked);
        console.log('autoTell set to ' + isChecked);
    });

    document.getElementById('autoSendCheckbox').addEventListener('change', function () {
        isAutoSendOn = this.checked;
        if (!isAutoSendOn) disableAutoSendForAllCodeBlocks();
        localStorage.setItem('autoSend', isAutoSendOn);
        console.log('autoSend set to ' + isAutoSendOn);
    });

    document.getElementById('sendMessageButton').addEventListener('click', () => {
        const messageToSend = document.getElementById('messageInput').value;
        SendCommand( messageToSend);
    });

    document.getElementById('saveLanguagesButton').addEventListener('click', () => {
        const allowedLanguages = document.getElementById('allowedLanguagesInput').value
            .split(',')
            .map(lang => lang.trim().toLowerCase());
        localStorage.setItem('allowedLanguages', JSON.stringify(allowedLanguages));
        console.log('Allowed languages set to: ', allowedLanguages);
    });
}


// Toggle settings popup
function toggleSettingsPopup() {
    const popup = document.getElementById('my-settings-popup');
    if (popup.style.display === 'none') {
        if (connection && connection.state !== signalR.HubConnectionState.Connected) {
            reconnectAttempts = 0;
            startReconnectInterval();
        }
    }
    popup.style.display = popup.style.display === 'block' ? 'none' : 'block';
}

// Mutation observer to track DOM changes
const observer = new MutationObserver((mutationsList) => {
    for (const mutation of mutationsList) {
        if (mutation.type === 'childList') {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    if (node.tagName === 'PRE') {
                        const codeBlock = node.querySelector('code.hljs');
                        if (codeBlock) {
                            console.log('Обработка нового сообщения');
                            appendSendButton(node); // Add send button to new message
                            if (isAutoSendOn) {
                                addTimerToCodeBlock(codeBlock); // Add timer if auto-send is enabled
                            }
                        }
                    }
                    // Handle existing messages or switching to an existing conversation
                    if ((node.tagName === 'DIV' && node.getAttribute('role') === 'presentation') ||
                        (node.tagName === 'DIV' && Array.from(node.classList).some(className => className.startsWith('react-scroll-to-bottom')))) {
                        console.log('Обработка старых сообщений');
                        createSettingsButton(); // Create settings button
                        createSettingsPopup(); // Create settings popup
                        addSendButtonToAllCodeBlocks(); // Add send button to all existing messages
                    }
                }
            });
        }
    }
    // Ensure chatgpt is defined before calling updateGenerationStatus
    if (typeof chatgpt !== 'undefined') {
        updateGenerationStatus();
    } else {
        console.warn("chatgpt is not defined yet.");
    }
});

// Start observing the DOM
observer.observe(document.body, { childList: true, subtree: true });

// Function to set the connection status
function SetConnectionStatus(status) {
    console.log(`Setting connection status to: ${status}`);
    localStorage.setItem('ConnectionStatus', status);
    const statusElement = document.getElementById('ConnectionStatus');
    if (statusElement) {
        statusElement.textContent = status;
    }
}


// Function to update the connection status in the DOM
function UpdateConnectionStatus(newValue) {
    const statusDiv = document.getElementById('ConnectionStatus');
    if (statusDiv) {
        statusDiv.textContent = newValue || 'Unknown';
        localStorage.setItem('connectionStatus', statusDiv.textContent);
    }
}

// Function to update the last received message in the DOM
function updateLastReceivedMessage(newValue) {
    const messageDiv = document.getElementById('lastReceivedMessage');
    if (messageDiv) {
        messageDiv.textContent = newValue || 'None';
        localStorage.setItem('lastReceivedMessage', messageDiv.textContent);
    }
}

// Function to update the generation status
async function updateGenerationStatus() {
    const isIdle = await chatgpt.isIdle();
    const status = isIdle ? "finished" : "progress";
    console.log(`Changed generation status to: ${status}`);
    document.body.dispatchEvent(new CustomEvent("generationStatusChanged", { detail: { status } }));
}

// Add send button to all existing code blocks on page load
addSendButtonToAllCodeBlocks();

console.log('script loaded');


