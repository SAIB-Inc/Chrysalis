package main

import (
	"fmt"
	"net"
	"os"
	"sync/atomic"
	"time"

	ouroboros "github.com/blinklabs-io/gouroboros"
	"github.com/blinklabs-io/gouroboros/ledger"
	"github.com/blinklabs-io/gouroboros/protocol/chainsync"
	ocommon "github.com/blinklabs-io/gouroboros/protocol/common"
)

type args struct {
	tcpHost string
	tcpPort string
	magic   uint32
	blocks  int64
	socket  string
}

func parseArgs() args {
	a := args{
		tcpHost: "127.0.0.1",
		tcpPort: "3001",
		magic:   2,
		blocks:  0, // 0 = unlimited
	}
	argv := os.Args[1:]
	for i := 0; i < len(argv); i++ {
		switch argv[i] {
		case "--tcp-host":
			i++
			a.tcpHost = argv[i]
		case "--tcp-port":
			i++
			a.tcpPort = argv[i]
		case "--magic":
			i++
			fmt.Sscanf(argv[i], "%d", &a.magic)
		case "--blocks":
			i++
			fmt.Sscanf(argv[i], "%d", &a.blocks)
		case "--socket":
			i++
			a.socket = argv[i]
		}
	}
	return a
}

var blockCount atomic.Int64
var lastSlot atomic.Int64
var lastHeight atomic.Int64
var tipSlot atomic.Int64

func main() {
	a := parseArgs()

	var conn net.Conn
	var err error
	var addr string

	if a.socket != "" {
		addr = a.socket
		conn, err = net.DialTimeout("unix", a.socket, 10*time.Second)
	} else {
		addr = fmt.Sprintf("%s:%s", a.tcpHost, a.tcpPort)
		conn, err = net.DialTimeout("tcp", addr, 10*time.Second)
	}
	if err != nil {
		fmt.Fprintf(os.Stderr, "connect error: %v\n", err)
		os.Exit(1)
	}
	defer conn.Close()

	isN2N := a.socket == ""

	mode := "N2N"
	if !isN2N {
		mode = "N2C"
	}

	if isN2N {
		fmt.Printf("Gouroboros ChainSync Benchmark (%s, headers only, pipeline 100)\n", mode)
	} else {
		fmt.Printf("Gouroboros ChainSync Benchmark (%s, full blocks)\n", mode)
	}
	fmt.Printf("  Address:  %s\n", addr)
	fmt.Printf("  Magic:    %d\n", a.magic)
	if a.blocks > 0 {
		fmt.Printf("  Target:   %d blocks\n", a.blocks)
	} else {
		fmt.Printf("  Target:   unlimited (Ctrl+C to stop)\n")
	}
	fmt.Println()

	var rollForwardFunc chainsync.RollForwardFunc
	pipelineLimit := 1 // N2C: sequential

	if isN2N {
		pipelineLimit = 100 // N2N: pipelined headers
		rollForwardFunc = func(ctx chainsync.CallbackContext, blockType uint, block interface{}, tip chainsync.Tip) error {
			blockCount.Add(1)
			tipSlot.Store(int64(tip.Point.Slot))
			if header, ok := block.(ledger.BlockHeader); ok {
				lastSlot.Store(int64(header.SlotNumber()))
				lastHeight.Store(int64(header.BlockNumber()))
			}
			return nil
		}
	} else {
		rollForwardFunc = func(ctx chainsync.CallbackContext, blockType uint, block interface{}, tip chainsync.Tip) error {
			blockCount.Add(1)
			tipSlot.Store(int64(tip.Point.Slot))
			if b, ok := block.(ledger.Block); ok {
				lastSlot.Store(int64(b.SlotNumber()))
				lastHeight.Store(int64(b.BlockNumber()))
			}
			return nil
		}
	}

	oConn, err := ouroboros.NewConnection(
		ouroboros.WithConnection(conn),
		ouroboros.WithNetworkMagic(a.magic),
		ouroboros.WithNodeToNode(isN2N),
		ouroboros.WithKeepAlive(isN2N),
		ouroboros.WithChainSyncConfig(
			chainsync.NewConfig(
				chainsync.WithPipelineLimit(pipelineLimit),
				chainsync.WithRollForwardFunc(rollForwardFunc),
				chainsync.WithRollBackwardFunc(func(ctx chainsync.CallbackContext, point ocommon.Point, tip chainsync.Tip) error {
					return nil
				}),
			),
		),
	)
	if err != nil {
		fmt.Fprintf(os.Stderr, "ouroboros error: %v\n", err)
		os.Exit(1)
	}
	defer oConn.Close()

	fmt.Println("Connected. Starting sync...")
	fmt.Println()

	err = oConn.ChainSync().Client.Sync([]ocommon.Point{})
	if err != nil {
		fmt.Fprintf(os.Stderr, "sync error: %v\n", err)
		os.Exit(1)
	}

	start := time.Now()
	ticker := time.NewTicker(3 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case err := <-oConn.ErrorChan():
			if err != nil {
				fmt.Fprintf(os.Stderr, "error: %v\n", err)
				os.Exit(1)
			}
		case <-ticker.C:
			cnt := blockCount.Load()
			slot := lastSlot.Load()
			height := lastHeight.Load()
			tip := tipSlot.Load()
			elapsed := time.Since(start)
			h := int(elapsed.Hours())
			m := int(elapsed.Minutes()) % 60
			s := int(elapsed.Seconds()) % 60
			rate := float64(cnt) / elapsed.Seconds()

			fmt.Printf("[%02d:%02d:%02d] slot %10d block %8d | %7.1f blk/s | %d total | tip slot %d\n",
				h, m, s, slot, height, rate, cnt, tip)

			if a.blocks > 0 && cnt >= a.blocks {
				fmt.Println()
				fmt.Printf("=== Summary ===\n")
				fmt.Printf("  Blocks synced:  %d\n", cnt)
				fmt.Printf("  Last slot:      %d\n", slot)
				fmt.Printf("  Total time:     %.1fs\n", elapsed.Seconds())
				fmt.Printf("  Avg blocks/s:   %.1f\n", rate)
				os.Exit(0)
			}
		}
	}
}
