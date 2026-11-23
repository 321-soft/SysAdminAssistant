<?php
// Copy to config.php OUTSIDE public_html/www. Restrict it to your hosting account (chmod 600).
return [
    'server_id' => 'assistant-server',
    'name' => 'SysAdmin Assistant',
    'language' => 'ru', // ru or en; Telegram notification language
    'secret' => 'REPLACE_WITH_64_HEX_CHARACTERS',
    'telegram_token' => 'REPLACE_WITH_BOT_TOKEN',
    'telegram_chat' => 'REPLACE_WITH_NUMERIC_CHAT_ID',
    'state_directory' => __DIR__,
    'maintenance' => false,
];
