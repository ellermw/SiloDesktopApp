<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>401 - Error | continuum.taverncdn.com</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Arial, sans-serif;
            background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
            color: #e0e0e0;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            align-items: center;
            padding: 40px 20px;
        }
        .container { max-width: 700px; width: 100%; }
        .error-header { text-align: center; margin-bottom: 40px; }
        .error-code { font-size: 72px; font-weight: 700; color: #f44336; }
        .error-title { font-size: 24px; color: #fff; margin: 10px 0 20px; }
        .host-display { color: #2196F3; font-size: 14px; }

        .status-diagram {
            display: flex;
            justify-content: center;
            align-items: center;
            gap: 20px;
            margin: 30px 0;
            padding: 20px;
            background: rgba(255,255,255,0.05);
            border-radius: 12px;
        }
        .status-item {
            text-align: center;
            flex: 0 0 auto;
            min-width: 80px;
            max-width: 150px;
        }
        .status-icon {
            width: 60px; height: 60px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 24px;
            margin: 0 auto 8px auto;
        }
        .status-ok { background: #4CAF50; }
        .status-error { background: #f44336; }
        .status-label {
            font-size: 12px;
            color: #888;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }
        .status-state { font-size: 11px; margin-top: 4px; }
        .status-state.ok { color: #4CAF50; }
        .status-state.error { color: #f44336; }
        .arrow { color: #444; font-size: 24px; }

        .card {
            background: rgba(255,255,255,0.08);
            border-radius: 12px;
            padding: 24px;
            margin-bottom: 20px;
            border: 1px solid rgba(255,255,255,0.1);
        }
        .card-title {
            font-size: 16px;
            font-weight: 600;
            color: #fff;
            margin-bottom: 12px;
            padding-left: 12px;
            border-left: 4px solid #2196F3;
        }
        .card-content { color: #b0b0b0; line-height: 1.6; }
        .card-content p { margin-bottom: 12px; }
        .card-content p:last-child { margin-bottom: 0; }

        .debug-info {
            background: rgba(0,0,0,0.3);
            border-radius: 8px;
            padding: 16px;
            font-family: monospace;
            font-size: 13px;
        }
        .debug-row { display: flex; margin-bottom: 8px; }
        .debug-row:last-child { margin-bottom: 0; }
        .debug-label { color: #888; min-width: 140px; }
        .debug-value { color: #4CAF50; word-break: break-all; }

        .retry-btn {
            display: inline-block;
            background: #2196F3;
            color: white;
            padding: 12px 32px;
            border-radius: 8px;
            text-decoration: none;
            font-weight: 500;
            margin-top: 20px;
            cursor: pointer;
            border: none;
            font-size: 16px;
        }
        .retry-btn:hover { background: #1976D2; }
        .footer { text-align: center; margin-top: 40px; color: #666; font-size: 13px; }

        @media (max-width: 600px) {
            .error-code { font-size: 48px; }
            .error-title { font-size: 18px; }
            .status-diagram { flex-wrap: wrap; }
            .debug-row { flex-direction: column; }
            .debug-label { min-width: auto; margin-bottom: 4px; }
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="error-header">
            <div class="error-code">401</div>
            <div class="error-title">Error</div>
            <div class="host-display">continuum.taverncdn.com</div>
        </div>

        <div class="status-diagram">
            <div class="status-item">
                <div class="status-icon status-ok">&#x1F4BB;</div>
                <div class="status-label">Browser</div>
                <div class="status-state ok">Working</div>
            </div>
            <div class="arrow">&#x27A1;</div>
            <div class="status-item">
                <div class="status-icon status-ok">&#x2601;</div>
                <div class="status-label">DataHorders</div>
                <div class="status-state ok">Working</div>
            </div>
            <div class="arrow">&#x27A1;</div>
            <div class="status-item">
                <div class="status-icon status-error">&#x1F5A5;</div>
                <div class="status-label">continuum.taverncdn.com</div>
                <div class="status-state error">Error</div>
            </div>
        </div>

        <div class="card">
            <div class="card-title">What happened?</div>
            <div class="card-content">An unexpected error occurred.</div>
        </div>

        <div class="card">
            <div class="card-title">What can I do?</div>
            <div class="card-content">
                <p><strong>If you're a visitor:</strong> Please try again.</p>
                <p><strong>If you're the site owner:</strong> Check your server logs for more information.</p>
            </div>
        </div>

        <div class="card">
            <div class="card-title">Debug Information</div>
            <div class="debug-info">
                <div class="debug-row">
                    <span class="debug-label">Request ID:</span>
                    <span class="debug-value">cdn-dal-01-69d34228-ce3bea3c</span>
                </div>
                <div class="debug-row">
                    <span class="debug-label">Timestamp:</span>
                    <span class="debug-value">2026-04-06 05:18:32 UTC</span>
                </div>
                <div class="debug-row">
                    <span class="debug-label">CDN Node:</span>
                    <span class="debug-value">cdn-dal-01</span>
                </div>
                <div class="debug-row">
                    <span class="debug-label">Backend Protocol:</span>
                    <span class="debug-value">HTTPS</span>
                </div>
            </div>
        </div>

        <div style="text-align: center;">
            <button onclick="location.reload()" class="retry-btn">Try Again</button>
        </div>

        <div class="footer">
            <p>Performance &amp; security by DataHorders CDN</p>
        </div>
    </div>
</body>
</html>
    