-- Устанавливаем параметр toast_tuple_target для таблицы snapshots
ALTER TABLE snapshots SET (toast_tuple_target = 4080);