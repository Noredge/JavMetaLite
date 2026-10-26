#!/usr/bin/env swift
// Numerical acceptance aid, not visual review. No images or pixel arrays are written.
// swift compare-image-output.swift --source reference.jpg --output poster.jpg --mode poster
// ImageIO decodes encoded pixels without applying EXIF orientation; poster uses the
// independent CoreGraphics right-half crop, not the production Skia implementation.
import Foundation
import CoreGraphics
import ImageIO

private enum ComparisonError: String, Error {
    case invalidArguments = "INVALID_ARGUMENTS"
    case unreadableInput = "UNREADABLE_INPUT"
    case encodedSizeLimit = "ENCODED_SIZE_LIMIT"
    case pixelLimit = "PIXEL_LIMIT"
    case unsupportedImage = "UNSUPPORTED_IMAGE"
    case decodeFailed = "DECODE_FAILED"
}

private let maximumPixels = 40_000_000
private let maximumEncodedBytes = 100 * 1024 * 1024
private let meanLimit = 5.0
private let percentile95Limit = 20

private struct Arguments {
    let source: String
    let output: String
    let mode: String

    init(_ values: [String]) throws {
        guard values.count == 6 else { throw ComparisonError.invalidArguments }
        var options: [String: String] = [:]
        for index in stride(from: 0, to: values.count, by: 2) {
            let key = values[index]
            guard ["--source", "--output", "--mode"].contains(key),
                  options[key] == nil, !values[index + 1].isEmpty else {
                throw ComparisonError.invalidArguments
            }
            options[key] = values[index + 1]
        }
        guard let source = options["--source"], let output = options["--output"],
              let mode = options["--mode"], ["full", "poster"].contains(mode) else {
            throw ComparisonError.invalidArguments
        }
        self.source = source; self.output = output; self.mode = mode
    }
}

private func decode(_ path: String) throws -> CGImage {
    let url = URL(fileURLWithPath: path)
    guard let attributes = try? FileManager.default.attributesOfItem(atPath: url.path),
          attributes[.type] as? FileAttributeType == .typeRegular,
          let bytes = attributes[.size] as? NSNumber else { throw ComparisonError.unreadableInput }
    guard bytes.int64Value > 0, bytes.int64Value <= maximumEncodedBytes else {
        throw ComparisonError.encodedSizeLimit
    }
    guard let source = CGImageSourceCreateWithURL(url as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary),
          let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
          let width = (properties[kCGImagePropertyPixelWidth] as? NSNumber)?.intValue,
          let height = (properties[kCGImagePropertyPixelHeight] as? NSNumber)?.intValue else {
        throw ComparisonError.unsupportedImage
    }
    guard width > 0, height > 0, width <= maximumPixels, height <= maximumPixels / width else {
        throw ComparisonError.pixelLimit
    }
    // Do not use thumbnail transforms: those can rotate using EXIF orientation.
    guard let image = CGImageSourceCreateImageAtIndex(source, 0, [kCGImageSourceShouldCache: true] as CFDictionary),
          image.width == width, image.height == height else { throw ComparisonError.decodeFailed }
    return image
}

private func normalizedPixels(_ image: CGImage, whiteBackground: Bool) throws -> [UInt8] {
    let rowBytes = image.width * 4
    var pixels = [UInt8](repeating: 0, count: rowBytes * image.height)
    let succeeded = pixels.withUnsafeMutableBytes { buffer -> Bool in
        guard let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: buffer.baseAddress, width: image.width, height: image.height,
                                      bitsPerComponent: 8, bytesPerRow: rowBytes, space: colorSpace,
                                      bitmapInfo: CGBitmapInfo.byteOrder32Big.rawValue | CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
        let bounds = CGRect(x: 0, y: 0, width: image.width, height: image.height)
        if whiteBackground {
            context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(bounds)
        }
        context.setRenderingIntent(.relativeColorimetric)
        context.interpolationQuality = .none
        context.draw(image, in: bounds)
        return true
    }
    guard succeeded else { throw ComparisonError.decodeFailed }
    return pixels
}

private func dimensions(_ image: CGImage) -> [String: Int] {
    ["width": image.width, "height": image.height]
}

private func compare(_ arguments: Arguments) throws -> [String: Any] {
    let source = try decode(arguments.source)
    let output = try decode(arguments.output)
    let cropWidth = arguments.mode == "poster" && source.width > source.height ? max(1, source.width / 2) : source.width
    let cropX = source.width - cropWidth
    var result: [String: Any] = [
        "source": dimensions(source), "output": dimensions(output),
        "expected": ["width": cropWidth, "height": source.height],
        "crop": ["x": cropX, "y": 0, "width": cropWidth, "height": source.height],
        "meanAbsErrorLimit": meanLimit, "percentile95AbsErrorLimit": percentile95Limit,
        "meanAbsError": NSNull(), "maxError": NSNull(), "percentile95AbsError": NSNull(),
        "percentile99AbsError": NSNull(), "alphaMaxError": NSNull(), "passed": false
    ]
    guard output.width == cropWidth, output.height == source.height else { return result }
    guard let expected = source.cropping(to: CGRect(x: cropX, y: 0, width: cropWidth, height: source.height)) else {
        throw ComparisonError.decodeFailed
    }
    // Match the documented JPEG conversion: transparent pixels are flattened on
    // white for opaque output. Alpha-bearing PNG outputs retain premultiplied alpha.
    let whiteBackground = [CGImageAlphaInfo.none, .noneSkipFirst, .noneSkipLast].contains(output.alphaInfo)
    let reference = try normalizedPixels(expected, whiteBackground: whiteBackground)
    let actual = try normalizedPixels(output, whiteBackground: whiteBackground)
    var histogram = [Int](repeating: 0, count: 256)
    var sum: Int64 = 0
    var maximum = 0
    var alphaMaximum = 0
    for index in stride(from: 0, to: reference.count, by: 4) {
        for channel in 0..<3 {
            let error = abs(Int(reference[index + channel]) - Int(actual[index + channel]))
            histogram[error] += 1; sum += Int64(error); maximum = max(maximum, error)
        }
        alphaMaximum = max(alphaMaximum, abs(Int(reference[index + 3]) - Int(actual[index + 3])))
    }
    let count = output.width * output.height * 3
    func percentile(_ fraction: Double) -> Int {
        let rank = Int(ceil(Double(count) * fraction))
        var cumulative = 0
        for (error, occurrences) in histogram.enumerated() {
            cumulative += occurrences
            if cumulative >= rank { return error }
        }
        return 255
    }
    let mean = Double(sum) / Double(count)
    let p95 = percentile(0.95)
    result["meanAbsError"] = mean
    result["maxError"] = maximum
    result["percentile95AbsError"] = p95
    result["percentile99AbsError"] = percentile(0.99)
    result["alphaMaxError"] = alphaMaximum
    result["whiteBackground"] = whiteBackground
    result["passed"] = mean <= meanLimit && p95 <= percentile95Limit && alphaMaximum <= 1
    return result
}

private func emit(_ value: [String: Any]) {
    let data = try! JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data([10]))
}

do {
    let result = try compare(Arguments(Array(CommandLine.arguments.dropFirst())))
    emit(result)
    exit(result["passed"] as? Bool == true ? 0 : 1)
} catch let error as ComparisonError {
    emit(["passed": false, "error": error.rawValue]); exit(2)
} catch {
    emit(["passed": false, "error": "COMPARISON_FAILED"]); exit(2)
}
