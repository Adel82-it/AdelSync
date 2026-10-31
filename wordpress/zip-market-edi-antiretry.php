<?php
/**
 * Plugin Name: ZIP-Market EDI Anti-Retry + AdelSync API
 * Description: Антиретрай обмена 1С + JSON API для приложения AdelSync (статус, cooldown, отбитые повторы, оценка нагрузки).
 * Version: 1.1.0
 * Author: Адель Гайнуллин
 */

defined( 'ABSPATH' ) || exit;

const ZM_EDI_ANTIRETRY_COOLDOWN = 2700; // 45 минут
const ZM_EDI_CHUNK_WINDOW       = 180;
const ZM_ADELSYNC_API_KEY       = 'WJoyWkfEO-EBqLwHI2CJMYxov-Dv71yr';

add_action( 'wp_loaded', 'zm_edi_antiretry_maybe_short_circuit', PHP_INT_MAX - 1 );
add_action( 'rest_api_init', 'zm_adelsync_register_routes' );

/**
 * REST: /wp-json/adelsync/v1/status
 */
function zm_adelsync_register_routes() {
	register_rest_route(
		'adelsync/v1',
		'/status',
		array(
			'methods'             => 'GET',
			'callback'            => 'zm_adelsync_status_response',
			'permission_callback' => 'zm_adelsync_permission',
		)
	);
}

/**
 * @param WP_REST_Request $request Request.
 * @return bool|WP_Error
 */
function zm_adelsync_permission( $request ) {
	$key = (string) $request->get_param( 'key' );
	if ( '' === $key ) {
		$key = (string) $request->get_header( 'x-adelsync-key' );
	}
	$expected = (string) apply_filters( 'zm_adelsync_api_key', ZM_ADELSYNC_API_KEY );
	if ( '' === $key || ! hash_equals( $expected, $key ) ) {
		return new WP_Error( 'adelsync_forbidden', 'Invalid API key', array( 'status' => 403 ) );
	}
	return true;
}

/**
 * @return array
 */
function zm_adelsync_get_stats() {
	$stats = get_option( '_zm_adelsync_stats', array() );
	if ( ! is_array( $stats ) ) {
		$stats = array();
	}
	$day = gmdate( 'Y-m-d' );
	return array(
		'day'              => isset( $stats['day'] ) ? (string) $stats['day'] : $day,
		'skips_today'      => isset( $stats['skips_today'] ) ? (int) $stats['skips_today'] : 0,
		'skips_total'      => isset( $stats['skips_total'] ) ? (int) $stats['skips_total'] : 0,
		'imports_today'    => isset( $stats['imports_today'] ) ? (int) $stats['imports_today'] : 0,
		'last_skip_at'     => isset( $stats['last_skip_at'] ) ? (int) $stats['last_skip_at'] : 0,
		'last_import_at'   => isset( $stats['last_import_at'] ) ? (int) $stats['last_import_at'] : 0,
		'last_activity_at' => isset( $stats['last_activity_at'] ) ? (int) $stats['last_activity_at'] : 0,
		'syncing'          => ! empty( $stats['syncing'] ),
	);
}

/**
 * @param array $stats Stats.
 */
function zm_adelsync_save_stats( array $stats ) {
	update_option( '_zm_adelsync_stats', $stats, false );
}

/**
 * Rotate day counters.
 *
 * @param array $stats Stats.
 * @return array
 */
function zm_adelsync_roll_day( array $stats ) {
	$day = gmdate( 'Y-m-d' );
	if ( ( $stats['day'] ?? '' ) !== $day ) {
		$stats['day']           = $day;
		$stats['skips_today']   = 0;
		$stats['imports_today'] = 0;
	}
	return $stats;
}

/**
 * @param string $type skip|import|sync_start|sync_end|info.
 * @param string $message Human message.
 * @param array  $meta Meta.
 */
function zm_adelsync_push_event( $type, $message, array $meta = array() ) {
	$events   = get_option( '_zm_adelsync_events', array() );
	if ( ! is_array( $events ) ) {
		$events = array();
	}
	array_unshift(
		$events,
		array(
			'ts'      => time(),
			'type'    => $type,
			'message' => $message,
			'meta'    => $meta,
		)
	);
	$events = array_slice( $events, 0, 80 );
	update_option( '_zm_adelsync_events', $events, false );
}

/**
 * @param int $seconds Seconds.
 * @return string
 */
function zm_adelsync_format_remaining( $seconds ) {
	$seconds = max( 0, (int) $seconds );
	$h       = intdiv( $seconds, 3600 );
	$m       = intdiv( $seconds % 3600, 60 );
	$s       = $seconds % 60;
	return sprintf( '%02d:%02d:%02d', $h, $m, $s );
}

/**
 * Estimate "CPU saved" gauge 0..100 from skips.
 *
 * @param int $skips_today Skips.
 * @return array{score:int,label:string}
 */
function zm_adelsync_cpu_gauge( $skips_today ) {
	// Грубая оценка: каждый отбитый re-parse ≈ заметный пик на shared-хостинге.
	$score = (int) min( 100, $skips_today * 8 );
	if ( $score >= 70 ) {
		$label = 'высокая экономия';
	} elseif ( $score >= 35 ) {
		$label = 'средняя экономия';
	} elseif ( $score > 0 ) {
		$label = 'есть эффект';
	} else {
		$label = 'ожидание обмена';
	}
	return array(
		'score' => $score,
		'label' => $label,
	);
}

/**
 * @return WP_REST_Response
 */
function zm_adelsync_status_response() {
	$cooldown = (int) apply_filters( 'zm_edi_antiretry_cooldown', ZM_EDI_ANTIRETRY_COOLDOWN );
	$state    = zm_edi_antiretry_get_state();
	$stats    = zm_adelsync_roll_day( zm_adelsync_get_stats() );
	$now      = time();

	$next_ts = 0;
	foreach ( $state['completed'] as $ts ) {
		$until = (int) $ts + $cooldown;
		if ( $until > $next_ts ) {
			$next_ts = $until;
		}
	}
	$remaining = max( 0, $next_ts - $now );

	$syncing = ! empty( $stats['syncing'] ) && ( $now - (int) $stats['last_activity_at'] ) < 120;
	if ( $syncing ) {
		$status       = 'syncing';
		$status_label = 'Идёт обмен с 1С';
	} elseif ( $remaining > 0 ) {
		$status       = 'cooldown';
		$status_label = 'Защита: ожидание окна';
	} else {
		$status       = 'idle';
		$status_label = 'Готов к обмену';
	}

	$gauge = zm_adelsync_cpu_gauge( (int) $stats['skips_today'] );
	$events = get_option( '_zm_adelsync_events', array() );
	if ( ! is_array( $events ) ) {
		$events = array();
	}

	return rest_ensure_response(
		array(
			'ok'                 => true,
			'product'            => 'AdelSync',
			'author'             => 'Адель Гайнуллин',
			'site'               => home_url( '/' ),
			'status'             => $status,
			'status_label'       => $status_label,
			'cooldown_seconds'   => $remaining,
			'cooldown_total'     => $cooldown,
			'cooldown_until'     => $remaining > 0 ? gmdate( 'c', $next_ts ) : null,
			'next_exchange_label'=> $remaining > 0 ? zm_adelsync_format_remaining( $remaining ) : 'сейчас',
			'stats'              => array(
				'skips_today'    => (int) $stats['skips_today'],
				'skips_total'    => (int) $stats['skips_total'],
				'imports_today'  => (int) $stats['imports_today'],
				'cpu_saved_score'=> $gauge['score'],
				'cpu_saved_label'=> $gauge['label'],
				'last_import_at' => $stats['last_import_at'] ? gmdate( 'c', (int) $stats['last_import_at'] ) : null,
				'last_skip_at'   => $stats['last_skip_at'] ? gmdate( 'c', (int) $stats['last_skip_at'] ) : null,
			),
			'events'             => array_slice( $events, 0, 25 ),
			'server_time'        => gmdate( 'c' ),
			'version'            => '1.1.0',
		)
	);
}

function zm_edi_antiretry_is_request() {
	if ( empty( $_SERVER['REQUEST_URI'] ) ) {
		return false;
	}
	$path = wp_parse_url( wp_unslash( $_SERVER['REQUEST_URI'] ), PHP_URL_PATH );
	return '/edi/1c' === $path;
}

function zm_edi_antiretry_authorize() {
	$edi  = (array) get_option( 'edi', array() );
	$user = isset( $edi['username'] ) ? (string) $edi['username'] : '';
	$pass = isset( $edi['password'] ) ? (string) $edi['password'] : '';
	$user_1c = isset( $_SERVER['PHP_AUTH_USER'] ) ? sanitize_text_field( wp_unslash( $_SERVER['PHP_AUTH_USER'] ) ) : '';
	$pass_1c = isset( $_SERVER['PHP_AUTH_PW'] ) ? sanitize_text_field( wp_unslash( $_SERVER['PHP_AUTH_PW'] ) ) : '';
	if ( '' === $user || '' === $pass || '' === $user_1c || '' === $pass_1c ) {
		return false;
	}
	return hash_equals( $user, $user_1c ) && hash_equals( $pass, $pass_1c );
}

function zm_edi_antiretry_get_state() {
	$state = get_option( '_zm_edi_antiretry', array() );
	if ( ! is_array( $state ) ) {
		$state = array();
	}
	return array(
		'completed'     => isset( $state['completed'] ) && is_array( $state['completed'] ) ? $state['completed'] : array(),
		'last_file'     => isset( $state['last_file'] ) && is_array( $state['last_file'] ) ? $state['last_file'] : array(),
		'prev_mode'     => isset( $state['prev_mode'] ) ? (string) $state['prev_mode'] : '',
		'prev_filename' => isset( $state['prev_filename'] ) ? (string) $state['prev_filename'] : '',
	);
}

function zm_edi_antiretry_save_state( array $state ) {
	update_option( '_zm_edi_antiretry', $state, false );
}

function zm_edi_antiretry_log( $message ) {
	if ( class_exists( '\BytePerfect\EDI\EDI' ) ) {
		\BytePerfect\EDI\EDI::log()->notice( '[anti-retry] ' . $message );
	}
}

function zm_edi_antiretry_exit_success( $message = 'success' ) {
	nocache_headers();
	header( 'Content-Type: text/plain; charset=utf-8' );
	// phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped
	echo $message;
	exit;
}

function zm_edi_antiretry_touch_activity( $syncing = true ) {
	$stats = zm_adelsync_roll_day( zm_adelsync_get_stats() );
	$was   = ! empty( $stats['syncing'] );
	$stats['last_activity_at'] = time();
	$stats['syncing']          = (bool) $syncing;
	zm_adelsync_save_stats( $stats );
	if ( $syncing && ! $was ) {
		zm_adelsync_push_event( 'sync_start', 'Начат обмен каталога с 1С' );
	}
}

function zm_edi_antiretry_maybe_short_circuit() {
	if ( ! zm_edi_antiretry_is_request() ) {
		return;
	}

	// phpcs:ignore WordPress.Security.NonceVerification.Recommended
	$mode = isset( $_REQUEST['mode'] ) ? sanitize_text_field( wp_unslash( $_REQUEST['mode'] ) ) : '';
	// phpcs:ignore WordPress.Security.NonceVerification.Recommended
	$type = isset( $_REQUEST['type'] ) ? sanitize_text_field( wp_unslash( $_REQUEST['type'] ) ) : '';
	// phpcs:ignore WordPress.Security.NonceVerification.Recommended
	$filename = isset( $_REQUEST['filename'] ) ? sanitize_text_field( wp_unslash( $_REQUEST['filename'] ) ) : '';

	if ( 'catalog' !== $type ) {
		return;
	}

	$cooldown     = (int) apply_filters( 'zm_edi_antiretry_cooldown', ZM_EDI_ANTIRETRY_COOLDOWN );
	$chunk_window = (int) apply_filters( 'zm_edi_antiretry_chunk_window', ZM_EDI_CHUNK_WINDOW );
	$now          = time();
	$state        = zm_edi_antiretry_get_state();

	foreach ( $state['completed'] as $file => $ts ) {
		if ( ( $now - (int) $ts ) > $cooldown ) {
			unset( $state['completed'][ $file ] );
		}
	}

	if ( in_array( $mode, array( 'checkauth', 'init', 'file', 'import' ), true ) ) {
		zm_edi_antiretry_touch_activity( true );
	}

	if ( 'file' === $mode && '' !== $filename ) {
		$state['last_file'][ $filename ] = $now;
		$state['prev_mode']              = $mode;
		$state['prev_filename']          = $filename;
		zm_edi_antiretry_save_state( $state );
		return;
	}

	if ( 'import' === $mode && '' !== $filename ) {
		if ( ! zm_edi_antiretry_authorize() ) {
			return;
		}

		$prev_filename = $state['prev_filename'];
		$is_unpack     = ( '' !== $prev_filename && '.zip' === substr( $prev_filename, -4 ) );

		if ( $is_unpack ) {
			$state['prev_mode']     = $mode;
			$state['prev_filename'] = $filename;
			zm_edi_antiretry_save_state( $state );
			return;
		}

		$last_xml  = get_option( '_edi_last_xml_entry', array() );
		$is_resume = is_array( $last_xml ) && ! empty( $last_xml );

		if ( $is_resume ) {
			$state['prev_mode']     = $mode;
			$state['prev_filename'] = $filename;
			zm_edi_antiretry_save_state( $state );
			return;
		}

		if ( isset( $state['completed'][ $filename ] ) && ( $now - (int) $state['completed'][ $filename ] ) < $cooldown ) {
			zm_edi_antiretry_log( "skip re-parse {$filename} (cooldown {$cooldown}s)" );

			$stats = zm_adelsync_roll_day( zm_adelsync_get_stats() );
			$stats['skips_today']++;
			$stats['skips_total']++;
			$stats['last_skip_at']     = $now;
			$stats['last_activity_at'] = $now;
			zm_adelsync_save_stats( $stats );
			$wait = max( 0, ( (int) $state['completed'][ $filename ] + $cooldown ) - $now );
			zm_adelsync_push_event(
				'skip',
				"Отбит повторный разбор: {$filename}. Следующий доступен через " . zm_adelsync_format_remaining( $wait ),
				array( 'filename' => $filename )
			);

			foreach ( $state['last_file'] as $zip => $ts ) {
				if ( ( $now - (int) $ts ) <= ( $chunk_window + 120 ) ) {
					$state['completed'][ $zip ] = $now;
				}
			}

			$state['prev_mode']     = $mode;
			$state['prev_filename'] = $filename;
			zm_edi_antiretry_save_state( $state );
			zm_edi_antiretry_exit_success( 'success' );
		}

		$state['completed'][ $filename ] = $now;
		foreach ( $state['last_file'] as $zip => $ts ) {
			if ( ( $now - (int) $ts ) <= ( $chunk_window + 120 ) ) {
				$state['completed'][ $zip ] = $now;
			}
		}

		$stats = zm_adelsync_roll_day( zm_adelsync_get_stats() );
		$stats['imports_today']++;
		$stats['last_import_at']   = $now;
		$stats['last_activity_at'] = $now;
		zm_adelsync_save_stats( $stats );
		zm_adelsync_push_event( 'import', "Разбор файла: {$filename}", array( 'filename' => $filename ) );

		$state['prev_mode']     = $mode;
		$state['prev_filename'] = $filename;
		zm_edi_antiretry_save_state( $state );
		return;
	}

	if ( 'success' === $mode || ( 'checkauth' === $mode && 'sale' === $type ) ) {
		$stats = zm_adelsync_roll_day( zm_adelsync_get_stats() );
		if ( ! empty( $stats['syncing'] ) ) {
			$stats['syncing'] = false;
			zm_adelsync_save_stats( $stats );
			zm_adelsync_push_event( 'sync_end', 'Обмен завершён или перешёл в паузу' );
		}
	}

	$state['prev_mode']     = $mode;
	$state['prev_filename'] = $filename;
	zm_edi_antiretry_save_state( $state );
}
